using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace OnYourMark.UI
{
    /// <summary>
    /// 교차 HUD 스왑 연출. 생체(Vitals) / 지도(Spatial) / 소모품(Reserves) 채널이 주인을 바꾸는 순간,
    /// 화면에 있던 데이터(고스트)가 찢어지며 바깥으로 빠져나가고, 새 데이터가 안쪽에서 띠별로 엇갈려 들어와
    /// '쪼'(오버슈트 후 딱 멈춤)로 고정된 뒤 새 소유자 색(내 것 = 라임, 파트너 것 = 라벤더)으로 한 번 번쩍인다.
    ///
    /// 연결:
    /// - OnYourMarkHud.ChannelSwapping(교환 합의 / 반환) 을 자동으로 구독한다.
    /// - 네트워크 계층에서는 Play(channel, toSelf) 또는 PlayCrossLink() 를 직접 부르면 된다
    ///   (예: CrossHudState.returnedParts.OnValueChanged, 파트너 첫 접속 — HudPlayerBridge가 호출).
    ///
    /// 성능:
    /// - 대기 중 비용 0: 메쉬 이펙트는 Params가 꺼져 있으면 바로 빠지고, Update는 재생 중인 모듈만 돈다.
    /// - 고스트(모듈 복제본)는 모듈마다 처음 한 번만 만들고 재사용한다. 노이즈 블록은 ObjectPool로 돌려 쓴다.
    /// - 연출 오브젝트는 중첩 Canvas 아래에 있어 매 프레임 움직여도 메인 HUD 캔버스 배칭을 다시 하지 않는다.
    /// - 프레임당 GC 할당 없음 (문자열·배열·클로저를 재생 중에 만들지 않는다).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudSwapGlitch : MonoBehaviour
    {
        [SerializeField] OnYourMarkHud hud;
        [Tooltip("채널 순서의 모듈 이름: 0 생체, 1 지도, 2 소모품, 3 스킬 (HudSkillPanel이 런타임에 만든다)")]
        [SerializeField] string[] moduleNames = { "Vitals", "Spatial", "Reserves", HudSkillPanel.ModuleName };

        [Header("타이밍 (초, unscaled)")]
        [SerializeField, Min(0.2f)] float duration = 0.56f;
        [Tooltip("고스트(이전 데이터)가 빠져나가는 시간")]
        [SerializeField, Min(0.05f)] float exitTime = 0.2f;
        [Tooltip("새 데이터가 들어오기 시작하는 시점")]
        [SerializeField, Min(0f)] float arriveStart = 0.05f;
        [Tooltip("새 데이터가 자리에 꽂히는 시점 ('쪼')")]
        [SerializeField, Min(0.1f)] float lockTime = 0.31f;
        [Tooltip("크로스 링크(세 채널 동시)에서 모듈 사이 간격")]
        [SerializeField, Min(0f)] float crossLinkStagger = 0.08f;

        [Header("모양")]
        [Tooltip("데이터가 이동하는 거리(px, 1920x1080 기준)")]
        [SerializeField] float travel = 150f;
        [SerializeField] float tear = 36f;
        [SerializeField] float split = 7f;
        [SerializeField, Range(0f, 0.3f)] float overshoot = 0.07f;
        [SerializeField, Min(2f)] float bandHeight = 7f;
        [Tooltip("모듈당 동시에 떠 있는 노이즈 블록 수 상한")]
        [SerializeField, Range(0, 16)] int noiseBlocks = 8;

        [Header("소리 (선택)")]
        [SerializeField] AudioSource sfxSource;
        [SerializeField] AudioClip swapClip;

        [Header("시연")]
        [Tooltip("hud.demoInput일 때 F7로 크로스 링크를 다시 재생")]
        [SerializeField] bool demoKey = true;

        /// <summary>(channel, toSelf) 연출 시작 / 끝.</summary>
        public event Action<int, bool> SwapStarted, SwapFinished;

        public bool IsPlaying => _playing > 0;

        sealed class Module
        {
            public int channel;
            public RectTransform root;
            public CanvasGroup group;
            public Graphic[] graphics;
            public RectTransform[] rects;
            public HudGlitchMesh[] effects;
            public readonly GlitchParams live = new GlitchParams();

            public RectTransform ghost;               // 처음 쓸 때 한 번 만들어 재사용
            public CanvasGroup ghostGroup;
            public Graphic[] ghostGraphics;
            public RectTransform[] ghostRects;
            public HudGlitchMesh[] ghostEffects;
            public readonly GlitchParams ghostP = new GlitchParams();

            public bool playing, toSelf, ghostOn;
            public float start, dir, nextBlock;
            public Vector2 size;
        }

        struct Block { public Image image; public float until; }

        readonly List<Module> _modules = new List<Module>(3);
        readonly List<Block> _blocks = new List<Block>(48);
        ObjectPool<Image> _blockPool;
        RectTransform _fx;
        int _playing;
        bool _ready;
        Color32 _primary = new Color32(0xB7, 0xA6, 0xF6, 255), _accent = new Color32(0xD5, 0xEF, 0x86, 255),
                _white = new Color32(0xF3, 0xEE, 0xDD, 255), _danger = new Color32(0xF7, 0x8E, 0x72, 255);

        void Reset() => hud = GetComponent<OnYourMarkHud>();

        void Awake()
        {
            if (!hud) hud = GetComponent<OnYourMarkHud>();
        }

        void OnEnable()
        {
            if (hud) hud.ChannelSwapping += OnChannelSwapping;
        }

        void OnDisable()
        {
            if (hud) hud.ChannelSwapping -= OnChannelSwapping;
            StopAll();
        }

        void OnDestroy() => _blockPool?.Dispose();

        void OnChannelSwapping(int channel, bool toSelf) => Play(channel, toSelf);

        // ------------------------------------------------------------------------------------------------ API

        /// <summary>한 채널 스왑. 반드시 데이터가 바뀌기 '직전'에 불러야 고스트에 이전 값이 남는다.</summary>
        public void Play(int channel, bool toSelf) => Play(channel, toSelf, 0f);

        /// <summary>세 채널을 차례로 뒤바꾼다 (매치 시작 / 파트너 접속: 서로의 HUD를 들고 시작하는 순간).</summary>
        public void PlayCrossLink(bool toSelf = false)
        {
            if (!EnsureReady()) return;
            for (int i = 0; i < _modules.Count; i++) Play(i, toSelf, i * crossLinkStagger, i == 0);
        }

        void Play(int channel, bool toSelf, float delay, bool withSound = true)
        {
            if (!EnsureReady() || channel < 0 || channel >= _modules.Count) return;
            var m = _modules[channel];
            if (m == null) return;

            if (!m.playing) _playing++;
            m.playing = true;
            m.toSelf = toSelf;
            m.start = Time.unscaledTime + delay;
            m.nextBlock = m.start;
            m.dir = OutwardDir(m.root);
            m.size = m.root.rect.size;
            int seed = UnityEngine.Random.Range(1, int.MaxValue);
            m.live.seed = seed; m.ghostP.seed = seed ^ 0x5bd1e995;

            CacheOffsets(m);
            SnapshotToGhost(m);
            Apply(m, 0f);   // 같은 프레임에 새 데이터가 그려지기 전에 가린다

            if (withSound && sfxSource && swapClip) sfxSource.PlayOneShot(swapClip);
            SwapStarted?.Invoke(channel, toSelf);
        }

        public void StopAll()
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                var m = _modules[i];
                if (m == null || !m.playing) continue;
                Finish(m);
            }
            for (int i = _blocks.Count - 1; i >= 0; i--) _blockPool.Release(_blocks[i].image);
            _blocks.Clear();
        }

        // ------------------------------------------------------------------------------------------------ update

        void LateUpdate()   // OnYourMarkHud.Update가 값(텍스트·색)을 쓴 뒤에 돈다
        {
            if (demoKey && hud && hud.demoInput)
            {
                var k = UnityEngine.InputSystem.Keyboard.current;
                if (k != null && k.f7Key.wasPressedThisFrame) PlayCrossLink();
            }
            if (_playing == 0 && _blocks.Count == 0) return;

            float now = Time.unscaledTime;
            for (int i = 0; i < _modules.Count; i++)
            {
                var m = _modules[i];
                if (m == null || !m.playing) continue;
                float u = now - m.start;
                if (u < 0f) continue;
                if (u >= duration) { Finish(m); continue; }
                Apply(m, u);
                SpawnBlocks(m, u, now);
            }
            UpdateBlocks(now);
        }

        void Apply(Module m, float u)
        {
            bool still = hud && hud.reducedMotion;
            float mTravel = still ? 0f : travel, mTear = still ? 0f : tear, mSplit = still ? 0f : split;
            int step = Mathf.FloorToInt(u * 30f);

            // --- 고스트: 이전 데이터가 찢어지며 바깥쪽(파트너 쪽)으로 빠진다
            var g = m.ghostP;
            float k = Mathf.Clamp01(u / exitTime);
            g.active = k < 1f;
            if (g.active)
            {
                g.step = step;
                g.slide = m.dir * mTravel * 1.25f * k * k;
                g.tear = mTear * (0.55f + 0.9f * k);
                g.split = mSplit * (1f + 1.2f * k);
                g.stagger = 0.7f;
                g.alpha = 1f - Smooth(0.3f, 1f, k);
                g.dropout = 0.12f + 0.5f * k;
                g.flash = 1f - Mathf.Clamp01(u / 0.05f);   // 첫 순간 흰 끊김
                g.flashColor = _white;
                g.bandHeight = bandHeight;
            }
            SetGhostVisible(m, g.active);

            // --- 새 데이터: 안쪽에서 띠별로 엇갈려 들어와 '쪼'로 꽂힌다
            var p = m.live;
            float a = Mathf.Clamp01((u - arriveStart) / Mathf.Max(0.01f, lockTime - arriveStart));
            p.active = true;
            p.step = step;
            p.bandHeight = bandHeight;
            p.slide = -m.dir * mTravel * (1f - Snap(a, overshoot));
            p.stagger = 0.9f * (1f - a);
            p.alpha = a <= 0f ? 0f : Mathf.Clamp01(a * 3.5f);
            p.dropout = a < 1f ? 0.3f * (1f - a) : 0f;
            p.tear = mTear * Mathf.Sqrt(1f - a);
            p.split = mSplit * 1.4f * (1f - a);
            // 꽂힌 뒤 여진 두 번 (아주 짧게)
            float after = u - lockTime;
            if (!still && ((after > 0.07f && after < 0.095f) || (after > 0.16f && after < 0.175f)))
            {
                p.tear = mTear * 0.22f;
                p.split = mSplit * 0.4f;
            }
            // 꽂히는 순간 흰 섬광 → 새 소유자 색으로 식는다
            p.flashColor = Color32.Lerp(new Color32(255, 255, 255, 255), m.toSelf ? _accent : _primary, Mathf.Clamp01(after / 0.05f));
            p.flash = after >= 0f ? Mathf.Exp(-after / 0.09f) : 0f;

            Dirty(m.graphics);
            if (g.active) Dirty(m.ghostGraphics);
        }

        void Finish(Module m)
        {
            m.playing = false;
            _playing = Mathf.Max(0, _playing - 1);
            m.live.active = false;
            m.ghostP.active = false;
            SetGhostVisible(m, false);
            Dirty(m.graphics);   // 깨끗한 메쉬로 한 번 다시 그린다
            SwapFinished?.Invoke(m.channel, m.toSelf);
        }

        // ------------------------------------------------------------------------------------------------ noise blocks

        void SpawnBlocks(Module m, float u, float now)
        {
            if (noiseBlocks <= 0 || (hud && hud.reducedMotion) || u > lockTime + 0.03f || now < m.nextBlock) return;
            m.nextBlock = now + 0.022f;

            if (_blocks.Count >= noiseBlocks * Mathf.Max(1, _playing)) return;

            var img = _blockPool.Get();
            var rt = img.rectTransform;
            float w = Mathf.Lerp(10f, 120f, Rand(m, 1) * Rand(m, 2));
            float h = Mathf.Lerp(2f, 9f, Rand(m, 3));
            float x = Mathf.Lerp(-0.25f, 1.1f, Rand(m, 4)) * m.size.x + m.dir * travel * 0.5f * Rand(m, 5);
            float y = Rand(m, 6) * m.size.y;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = m.root.anchoredPosition + new Vector2(x, -y);
            float pick = Rand(m, 7);
            Color32 c = pick < 0.5f ? _primary : pick < 0.8f ? (m.toSelf ? _accent : _white) : _danger;
            c.a = (byte)Mathf.Lerp(60f, 190f, Rand(m, 8));
            img.color = c;
            _blocks.Add(new Block { image = img, until = now + Mathf.Lerp(0.03f, 0.085f, Rand(m, 9)) });
        }

        void UpdateBlocks(float now)
        {
            for (int i = _blocks.Count - 1; i >= 0; i--)
            {
                if (now < _blocks[i].until) continue;
                _blockPool.Release(_blocks[i].image);
                int last = _blocks.Count - 1;
                _blocks[i] = _blocks[last];
                _blocks.RemoveAt(last);
            }
        }

        int _randCounter;
        float Rand(Module m, int salt) => HudGlitchMesh.Hash(m.live.seed, ++_randCounter, salt);

        // ------------------------------------------------------------------------------------------------ setup

        bool EnsureReady()
        {
            if (_ready) return true;
            if (!hud || !Application.isPlaying) return false;

            ReadPalette();

            // 연출 레이어: HUD와 같은 좌표계(전체 늘림)의 중첩 캔버스. 움직이는 연출이 메인 캔버스를 다시 굽지 않게 분리한다.
            var go = new GameObject("Swap Glitch FX", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.hideFlags = HideFlags.DontSave;
            go.layer = hud.gameObject.layer;
            _fx = (RectTransform)go.transform;
            _fx.SetParent(hud.transform, false);
            _fx.anchorMin = Vector2.zero; _fx.anchorMax = Vector2.one;
            _fx.offsetMin = _fx.offsetMax = Vector2.zero;
            _fx.pivot = new Vector2(0f, 1f);
            _fx.SetAsLastSibling();
            var cg = go.GetComponent<CanvasGroup>();
            cg.interactable = false; cg.blocksRaycasts = false;

            _blockPool = new ObjectPool<Image>(CreateBlock,
                img => { img.enabled = true; img.transform.SetAsLastSibling(); },
                img => img.enabled = false,
                img => { if (img) Destroy(img.gameObject); },
                collectionCheck: false, defaultCapacity: 24, maxSize: 64);
            // 미리 데워 둔다: 첫 스왑에서 GameObject 생성이 몰리지 않게
            var warm = ListPool<Image>.Get();
            for (int i = 0; i < 24; i++) warm.Add(_blockPool.Get());
            for (int i = 0; i < warm.Count; i++) _blockPool.Release(warm[i]);
            ListPool<Image>.Release(warm);

            for (int c = 0; c < moduleNames.Length; c++)
            {
                var root = hud.transform.Find(moduleNames[c]) as RectTransform;
                if (!root) { _modules.Add(null); Debug.LogWarning($"[HudSwapGlitch] module '{moduleNames[c]}' not found under {hud.name}", this); continue; }
                var m = new Module { channel = c, root = root, group = root.GetComponent<CanvasGroup>() };
                m.graphics = root.GetComponentsInChildren<Graphic>(true);
                m.rects = new RectTransform[m.graphics.Length];
                m.effects = new HudGlitchMesh[m.graphics.Length];
                for (int i = 0; i < m.graphics.Length; i++)
                {
                    m.rects[i] = m.graphics[i].rectTransform;
                    var fx = m.graphics[i].GetComponent<HudGlitchMesh>();
                    if (!fx) fx = m.graphics[i].gameObject.AddComponent<HudGlitchMesh>();
                    fx.hideFlags = HideFlags.DontSave;
                    fx.Params = m.live;
                    m.effects[i] = fx;
                }
                BuildGhost(m);
                _modules.Add(m);
            }
            _ready = true;
            return true;
        }

        void BuildGhost(Module m)
        {
            // 라이브 모듈을 한 번 복제해 고스트로 재사용한다 (HudGlitchMesh도 같이 복제되지만 Params는 직렬화되지 않는다)
            var copy = Instantiate(m.root.gameObject, _fx, false);
            copy.name = m.root.name + " (swap ghost)";
            copy.hideFlags = HideFlags.DontSave;
            m.ghost = (RectTransform)copy.transform;
            m.ghostGroup = copy.GetComponent<CanvasGroup>();
            if (m.ghostGroup) { m.ghostGroup.interactable = false; m.ghostGroup.blocksRaycasts = false; }
            foreach (var b in copy.GetComponentsInChildren<Selectable>(true)) Destroy(b);
            m.ghostGraphics = copy.GetComponentsInChildren<Graphic>(true);
            m.ghostRects = new RectTransform[m.ghostGraphics.Length];
            m.ghostEffects = new HudGlitchMesh[m.ghostGraphics.Length];
            for (int i = 0; i < m.ghostGraphics.Length; i++)
            {
                var gg = m.ghostGraphics[i];
                gg.raycastTarget = false;
                m.ghostRects[i] = gg.rectTransform;
                var fx = gg.GetComponent<HudGlitchMesh>();
                if (!fx) fx = gg.gameObject.AddComponent<HudGlitchMesh>();
                fx.hideFlags = HideFlags.DontSave;
                fx.Params = m.ghostP;
                m.ghostEffects[i] = fx;
            }
            copy.SetActive(false);
        }

        Image CreateBlock()
        {
            var go = new GameObject("glitch block", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.hideFlags = HideFlags.DontSave;
            go.layer = _fx.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(_fx, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.material = hud.hologramMaterial;
            img.enabled = false;
            return img;
        }

        void ReadPalette()
        {
            if (!hud.layoutAsset) return;
            var layout = JsonUtility.FromJson<HudLayout>(hud.layoutAsset.text);
            if (layout?.colors == null) return;
            Parse(layout.colors.primary, ref _primary);
            Parse(layout.colors.accent, ref _accent);
            Parse(layout.colors.white, ref _white);
            Parse(layout.colors.danger, ref _danger);
        }

        static void Parse(string html, ref Color32 into)
        {
            if (!string.IsNullOrEmpty(html) && ColorUtility.TryParseHtmlString(html, out var c)) into = c;
        }

        // ------------------------------------------------------------------------------------------------ helpers

        void CacheOffsets(Module m)
        {
            for (int i = 0; i < m.effects.Length; i++)
            {
                Vector3 local = m.root.InverseTransformPoint(m.rects[i].TransformPoint(Vector3.zero));
                m.effects[i].ToModule = local;
                if (m.ghostEffects != null && i < m.ghostEffects.Length) m.ghostEffects[i].ToModule = local;
            }
        }

        /// <summary>라이브 모듈의 '지금' 모습(바뀌기 직전 값)을 고스트에 옮긴다. 참조 복사뿐이라 할당이 없다.</summary>
        void SnapshotToGhost(Module m)
        {
            if (!m.ghost) return;
            m.ghost.anchoredPosition3D = m.root.anchoredPosition3D;   // HudPerspective가 3D로 기울인 모듈에도 그대로 겹친다
            m.ghost.localRotation = m.root.localRotation;
            m.ghost.sizeDelta = m.root.sizeDelta;
            m.ghost.localScale = m.root.localScale;
            if (m.ghostGroup) m.ghostGroup.alpha = m.group ? m.group.alpha : 1f;
            int n = Mathf.Min(m.graphics.Length, m.ghostGraphics.Length);
            for (int i = 0; i < n; i++)
            {
                Graphic src = m.graphics[i], dst = m.ghostGraphics[i];
                if (dst.gameObject.activeSelf != src.gameObject.activeSelf) dst.gameObject.SetActive(src.gameObject.activeSelf);
                dst.enabled = src.enabled;
                dst.color = src.color;
                m.ghostRects[i].anchoredPosition = m.rects[i].anchoredPosition;
                m.ghostRects[i].sizeDelta = m.rects[i].sizeDelta;
                if (src is Text st && dst is Text dt) { if (!ReferenceEquals(dt.text, st.text)) dt.text = st.text; }
                else if (src is HudGeometry sg && dst is HudGeometry dg && sg.iconName != dg.iconName)
                {
                    dg.iconName = sg.iconName; dg.strokes = sg.strokes; dg.iconWidth = sg.iconWidth; dg.iconHeight = sg.iconHeight;
                    dg.Refresh();
                }
            }
        }

        void SetGhostVisible(Module m, bool on)
        {
            if (!m.ghost || m.ghostOn == on) return;
            m.ghostOn = on;
            m.ghost.gameObject.SetActive(on);
        }

        float OutwardDir(RectTransform module)
        {
            var hudRect = (RectTransform)hud.transform;
            Vector3 c = hudRect.InverseTransformPoint(module.TransformPoint(module.rect.center));
            return c.x < hudRect.rect.center.x ? -1f : 1f;
        }

        static void Dirty(Graphic[] graphics)
        {
            if (graphics == null) return;
            for (int i = 0; i < graphics.Length; i++) if (graphics[i] && graphics[i].isActiveAndEnabled) graphics[i].SetVerticesDirty();
        }

        static float Smooth(float a, float b, float x)
        {
            x = Mathf.Clamp01((x - a) / (b - a));
            return x * x * (3f - 2f * x);
        }

        /// <summary>'쪼': 72%까지 cubic-out으로 1+ov까지 갔다가, 남은 구간에 smoothstep으로 1로 돌아와 멈춘다.</summary>
        static float Snap(float t, float ov)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float k = 0.72f;
            if (t < k) { float x = 1f - t / k; return (1f + ov) * (1f - x * x * x); }
            float y = (t - k) / (1f - k);
            return 1f + ov * (1f - y * y * (3f - 2f * y));
        }
    }
}
