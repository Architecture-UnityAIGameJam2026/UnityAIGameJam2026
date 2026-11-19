using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark.UI
{
    /// <summary>
    /// 3번 파츠(스킬) 모듈. 반환 전에는 스킬 키가 파트너와 엮여 있으므로(내 Q가 파트너의 궁극기를 발동하고,
    /// 그 게이지는 내 처치로 찬다) 파트너 유닛의 스킬을 보여 주고, 반환 후에는 내 스킬을 보여 준다.
    /// 오버워치식으로 무기 칸 왼쪽에 [Q][E] 타일이 한 줄로 선다 (교환 · 지원 슬롯과 같은 유리 타일).
    ///   Q  궁극기: 충전 중에는 킬 수 + 아래 게이지, 준비되면 번개가 라임으로 숨쉬고, 발동 중에는 남은 초와 줄어드는 게이지.
    ///   E  패링(방패) 또는 무기 전환: 준비면 아이콘, 쿨타임이면 흐린 아이콘 위에 남은 초 + 차오르는 게이지, 발동 중이면 라임.
    ///   소유 아이콘: 라벤더 두 사람 = 파트너 것, 라임 한 사람 = 내 것 (다른 모듈과 같다).
    /// 레이아웃 JSON에 없는 모듈이라 런타임에 같은 팔레트 · 폰트 · 머티리얼 · 아이콘으로 직접 만든다 (저장되지 않음).
    /// 게임에서는 HudPlayerBridge가 SetTelemetry로 채운다. hud.demoInput이면 시연 값이 돌고 F6이 반환 ↔ 교차를 바꾼다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudSkillPanel : MonoBehaviour
    {
        public enum ESkill { None, Parry, WeaponSwitch }

        [Serializable]
        public struct Telemetry
        {
            public bool valid;
            public int kills, killsRequired;
            public bool ultReady, ultActive;
            public float ultLeft, ultDuration;
            public ESkill eSkill;
            public bool eActive;
            public float eCooldownLeft, eCooldown;
        }

        public const string ModuleName = "Skills";

        [SerializeField] OnYourMarkHud hud;
        [Tooltip("1920×1080 기준 좌상단 위치 (무기 칸 왼쪽, 같은 줄)")]
        [SerializeField] Vector2 position = new Vector2(1344f, 948f);
        [SerializeField] Vector2 size = new Vector2(196f, 92f);
        [Header("시연")]
        [Tooltip("hud.demoInput일 때 시연 값을 돌리고 F6으로 반환 ↔ 교차를 바꾼다")]
        [SerializeField] bool demoValues = true;

        /// <summary>표시 주인이 바뀌기 직전 (화면에는 아직 이전 값). true면 이제 내 스킬. HudBreak가 구독한다.</summary>
        public event Action<bool> OwnerSwapping;

        public bool Own => _own;
        public RectTransform Root => _root;

        RectTransform _root;
        CanvasGroup _group;
        HudGeometry _owner;
        Color _primary = new Color32(0xB7, 0xA6, 0xF6, 255), _dim = new Color32(0x71, 0x6A, 0x91, 255),
              _white = new Color32(0xF3, 0xEE, 0xDD, 255), _accent = new Color32(0xD5, 0xEF, 0x86, 255),
              _ink = new Color32(0x14, 0x13, 0x22, 255);

        const float TileW = 72f, TileH = 72f, BarW = 48f;

        sealed class Tile
        {
            public HudGeometry shade, contour, icon;
            public Graphic latch, track, bar;
            public Text number, key;
        }
        Tile _q, _e;
        readonly Dictionary<string, HudIconDefinition> _icons = new Dictionary<string, HudIconDefinition>();
        bool _own, _ownSet, _built;
        float _started;
        Telemetry _t;
        float _demoClock;

        static readonly string[] Two = BuildTwo();
        static string[] BuildTwo() { var a = new string[100]; for (int i = 0; i < a.Length; i++) a[i] = i.ToString("00"); return a; }

        void Reset() => hud = GetComponent<OnYourMarkHud>();

        void Awake()
        {
            if (!hud) hud = GetComponent<OnYourMarkHud>();
            Build();
        }

        void OnEnable()
        {
            var hb = GetComponent<HudBreak>();
            if (hb) hb.Watch(this);
        }

        void OnDestroy()
        {
            if (_root) Destroy(_root.gameObject);
        }

        // ------------------------------------------------------------------------------------------------ API

        /// <summary>
        /// 매 프레임 불러도 된다. own이 바뀌는 순간 OwnerSwapping을 먼저 부른 뒤(이전 화면이 고스트로 남는다) 새 값을 쓴다.
        /// </summary>
        public void SetTelemetry(in Telemetry t, bool own)
        {
            if (!_built) Build();
            if (!_ownSet) { _own = own; _ownSet = true; }
            else if (own != _own)
            {
                OwnerSwapping?.Invoke(own);
                _own = own;
            }
            _t = t;
        }

        // ------------------------------------------------------------------------------------------------ build

        void Build()
        {
            if (_built || !Application.isPlaying || !hud || !hud.layoutAsset) return;
            var layout = JsonUtility.FromJson<HudLayout>(hud.layoutAsset.text);
            if (layout?.colors != null)
            {
                Parse(layout.colors.primary, ref _primary);
                Parse(layout.colors.dim, ref _dim);
                Parse(layout.colors.white, ref _white);
                Parse(layout.colors.accent, ref _accent);
                Parse(layout.colors.ink, ref _ink);
            }
            _icons.Clear();
            if (layout?.icons != null) foreach (var icon in layout.icons) _icons[icon.name] = icon;

            var existing = hud.transform.Find(ModuleName) as RectTransform;
            if (existing) Destroy(existing.gameObject);
            _root = OnYourMarkHud.Rect(ModuleName, hud.transform, position.x, position.y, size.x, size.y);
            _root.gameObject.hideFlags = HideFlags.DontSave;
            _root.gameObject.layer = hud.gameObject.layer;
            var reserves = hud.transform.Find("Reserves");
            if (reserves) _root.SetSiblingIndex(reserves.GetSiblingIndex() + 1);
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f; _group.interactable = false; _group.blocksRaycasts = false;

            _owner = Icon("Ownership icon", "partner", 0f, 26f, 20f, 20f, 1.8f, _primary, false);
            _q = MakeTile("Q", 28f, "bolt");
            _e = MakeTile("E", 28f + TileW + 12f, "shield");

            _started = Time.unscaledTime;
            _built = true;
            if (hud.demoInput && demoValues) { _own = false; _ownSet = true; }
        }

        Tile MakeTile(string key, float x, string icon)
        {
            var t = new Tile();
            Color shade = _ink; shade.a = 0.35f;
            var outline = RoundRect(x, 0f, TileW, TileH, 11f, 6);
            t.shade = Shape(key + " shade", "polygon", outline, 1f, shade);
            t.contour = Shape(key + " contour", "line", Close(outline), 1.3f, _primary);
            t.latch = Box(key + " latch", x + TileW * 0.5f - 6f, 6f, 12f, 2f, _accent, true);
            t.icon = Icon(key + " icon", icon, x + TileW * 0.5f - 15f, 18f, 30f, 30f, 1.8f, _white, false);
            t.number = Label(key + " number", x, 14f, TileW, 40f, hud.displayFont, 30, _white, "00");
            t.number.alignment = TextAnchor.UpperCenter;
            t.track = Box(key + " track", x + (TileW - BarW) * 0.5f, 58f, BarW, 2f, _dim, false);
            t.bar = Box(key + " bar", x + (TileW - BarW) * 0.5f, 58f, BarW, 2f, _primary, true);
            t.key = Label(key + " key", x, 75f, TileW, 17f, hud.monoFont, 13, _primary, key);
            t.key.alignment = TextAnchor.UpperCenter;
            return t;
        }

        HudGeometry Shape(string n, string shape, float[] points, float stroke, Color c)
        {
            var g = Node(n, 0f, 0f, size.x, size.y).gameObject.AddComponent<HudGeometry>();
            g.shape = shape; g.points = points; g.thickness = stroke;
            g.color = c; g.raycastTarget = false; g.material = hud.hologramMaterial;
            return g;
        }

        /// <summary>둥근 사각형 외곽 점 (좌상단 기준, 아래로 y).</summary>
        static float[] RoundRect(float x, float y, float w, float h, float r, int seg)
        {
            var pts = new List<float>((seg + 1) * 8);
            Vector2[] centers = { new Vector2(x + w - r, y + r), new Vector2(x + w - r, y + h - r), new Vector2(x + r, y + h - r), new Vector2(x + r, y + r) };
            for (int k = 0; k < 4; k++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = (-90f + 90f * k + 90f * i / seg) * Mathf.Deg2Rad;
                    pts.Add(centers[k].x + Mathf.Cos(a) * r);
                    pts.Add(centers[k].y + Mathf.Sin(a) * r);
                }
            return pts.ToArray();
        }

        static float[] Close(float[] p)
        {
            var c = new float[p.Length + 2];
            p.CopyTo(c, 0);
            c[p.Length] = p[0]; c[p.Length + 1] = p[1];
            return c;
        }

        RectTransform Node(string n, float x, float y, float w, float h)
        {
            var rt = OnYourMarkHud.Rect(n, _root, x, y, w, h);
            rt.gameObject.hideFlags = HideFlags.DontSave;
            rt.gameObject.layer = _root.gameObject.layer;
            return rt;
        }

        Text Label(string n, float x, float y, float w, float h, Font font, int fontSize, Color c, string text)
        {
            var t = Node(n, x, y, w, h).gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = fontSize; t.text = text; t.color = c;
            t.supportRichText = false; t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.material = hud.hologramMaterial;
            return t;
        }

        HudGeometry Icon(string n, string icon, float x, float y, float w, float h, float stroke, Color c, bool glow)
        {
            var g = Node(n, x, y, w, h).gameObject.AddComponent<HudGeometry>();
            g.shape = "icon"; g.thickness = stroke;
            SetIcon(g, icon);
            g.color = c; g.raycastTarget = false;
            g.material = glow && hud.accentMaterial ? hud.accentMaterial : hud.hologramMaterial;
            return g;
        }

        Graphic Box(string n, float x, float y, float w, float h, Color c, bool glow)
        {
            var img = Node(n, x, y, w, h).gameObject.AddComponent<Image>();
            img.color = c; img.raycastTarget = false;
            img.material = glow && hud.accentMaterial ? hud.accentMaterial : hud.hologramMaterial;
            return img;
        }

        void SetIcon(HudGeometry g, string icon)
        {
            if (g.iconName == icon || !_icons.TryGetValue(icon, out var d)) return;
            g.iconName = icon; g.strokes = d.strokes; g.iconWidth = d.width; g.iconHeight = d.height;
            g.Refresh();
        }

        static void Parse(string html, ref Color into)
        {
            if (!string.IsNullOrEmpty(html) && ColorUtility.TryParseHtmlString(html, out var c)) into = c;
        }

        // ------------------------------------------------------------------------------------------------ update

        void Update()
        {
            if (!_built || !hud || !hud.demoInput || !demoValues) return;
            var k = UnityEngine.InputSystem.Keyboard.current;
            bool own = _own;
            if (k != null && k.f6Key.wasPressedThisFrame) own = !own;
            _demoClock += Time.unscaledDeltaTime;
            SetTelemetry(own ? DemoSelf(_demoClock) : DemoPartner(_demoClock), own);
        }

        void LateUpdate()
        {
            if (!_built) return;
            float intro = hud && hud.reducedMotion ? 1f : Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - _started) / 0.55f);
            if (_group.alpha != intro) _group.alpha = intro;
            Render(_t);
        }

        void Render(in Telemetry t)
        {
            float now = Time.unscaledTime;

            // Q 궁극기
            if (!t.valid) Paint(_q, "bolt", null, 0f, _dim, _dim, 0.25f, false);
            else if (t.ultActive)
                Paint(_q, null, Two[Mathf.Clamp(Mathf.CeilToInt(t.ultLeft), 0, 99)], Mathf.Clamp01(t.ultLeft / Mathf.Max(0.01f, t.ultDuration)), _accent, _accent, 1f, true);
            else if (t.ultReady)
            {
                float pulse = 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 2f * 1.4f));   // Q를 누르라는 숨쉬기
                Paint(_q, "bolt", null, 1f, _accent, _accent, pulse, true);
            }
            else
            {
                int req = Mathf.Max(1, t.killsRequired);
                Paint(_q, null, Two[Mathf.Clamp(t.kills, 0, 99)], (float)Mathf.Clamp(t.kills, 0, req) / req, _white, _primary, 1f, false);
            }

            // E 스킬
            string eIcon = t.eSkill == ESkill.WeaponSwitch ? "relay" : "shield";
            if (!t.valid || t.eSkill == ESkill.None) Paint(_e, eIcon, null, 0f, _dim, _dim, 0.25f, false);
            else if (t.eActive)
            {
                float blink = t.eSkill == ESkill.WeaponSwitch && Mathf.Repeat(now * 6f, 1f) < 0.35f ? 0.45f : 1f;   // 변형 중 깜빡
                Paint(_e, eIcon, null, 1f, _accent, _accent, blink, true);
            }
            else if (t.eCooldownLeft > 0f)
            {
                Paint(_e, eIcon, Two[Mathf.Clamp(Mathf.CeilToInt(t.eCooldownLeft), 0, 99)],
                    1f - Mathf.Clamp01(t.eCooldownLeft / Mathf.Max(0.01f, t.eCooldown)), _white, _primary, 1f, false);
                var dimIcon = _primary; dimIcon.a = 0.22f; _e.icon.color = dimIcon;   // 흐린 아이콘 위에 남은 초
            }
            else Paint(_e, eIcon, null, 1f, _white, _primary, 1f, false);

            SetIcon(_owner, _own ? "self" : "partner");
            _owner.color = _own ? _accent : _primary;
        }

        /// <summary>타일 하나: icon이 있으면 아이콘을 보이고, number가 있으면 숫자를 보인다 (둘 다면 숫자를 아이콘 위에 겹친다).</summary>
        void Paint(Tile k, string icon, string number, float fill, Color fg, Color frame, float alpha, bool glow)
        {
            bool showIcon = icon != null;
            if (showIcon) SetIcon(k.icon, icon);
            if (k.icon.enabled != showIcon) k.icon.enabled = showIcon;
            Color ic = fg; ic.a *= alpha; k.icon.color = ic;
            k.icon.material = glow && hud.accentMaterial ? hud.accentMaterial : hud.hologramMaterial;

            bool showNum = number != null;
            if (k.number.enabled != showNum) k.number.enabled = showNum;
            if (showNum && k.number.text != number) k.number.text = number;
            k.number.color = fg;

            Color c = frame; c.a = (glow ? 1f : 0.85f) * alpha; k.contour.color = c;
            k.contour.material = glow && hud.accentMaterial ? hud.accentMaterial : hud.hologramMaterial;
            if (k.latch.enabled != glow) k.latch.enabled = glow;

            var bar = k.bar.rectTransform.sizeDelta;
            float w = Mathf.Round(BarW * Mathf.Clamp01(fill));
            if (bar.x != w) { bar.x = w; k.bar.rectTransform.sizeDelta = bar; }
            if (k.bar.enabled != (w > 0.5f)) k.bar.enabled = w > 0.5f;
            Color b = glow ? _accent : _primary; b.a = 0.85f * alpha; k.bar.color = b;
            Color tr = _dim; tr.a = 0.35f; k.track.color = tr;
            k.key.color = frame;
        }

        // ------------------------------------------------------------------------------------------------ demo

        // 파트너(검): 킬이 차고 → 준비 → 발동 6초, 패링 8초 쿨
        static Telemetry DemoPartner(float t)
        {
            float phase = Mathf.Repeat(t, 17f);
            var d = new Telemetry { valid = true, killsRequired = 15, ultDuration = 6f, eSkill = ESkill.Parry, eCooldown = 8f };
            if (phase < 9f) d.kills = Mathf.Min(14, Mathf.FloorToInt(phase * 1.6f));
            else if (phase < 11f) { d.kills = 15; d.ultReady = true; }
            else { d.ultActive = true; d.ultLeft = 17f - phase; }
            float e = Mathf.Repeat(t + 3f, 8f);
            if (e < 0.8f) d.eActive = true; else d.eCooldownLeft = 8f - e;
            return d;
        }

        // 나(총): 무기 전환 2초 쿨, 킬 게이지는 따로
        static Telemetry DemoSelf(float t)
        {
            var d = new Telemetry { valid = true, killsRequired = 12, kills = Mathf.Min(11, 4 + Mathf.FloorToInt(Mathf.Repeat(t, 9f))), ultDuration = 8f, eSkill = ESkill.WeaponSwitch, eCooldown = 2f };
            float e = Mathf.Repeat(t, 5f);
            if (e < 0.6f) d.eActive = true; else if (e < 2.6f) d.eCooldownLeft = 2.6f - e;
            return d;
        }
    }
}
