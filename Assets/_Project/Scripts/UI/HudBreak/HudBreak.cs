using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark.UI
{
    public enum HudBreakSeverity { Light, Medium, Heavy }

    /// <summary>HudBreak가 고장 낼 수 있는 모듈. 0..2는 OnYourMarkHud 채널 순서와 같다.</summary>
    public enum HudModule { All = -1, Vitals = 0, Spatial = 1, Reserves = 2, Skills = 3 }

    /// <summary>
    /// HUD 피해 글리치. 교환 글리치(HudSwapGlitch)와 같은 메쉬 방식에, 적 파괴 연출(OYM.ShatterFX)의 시각 언어를 입힌다:
    /// 맞는 순간 HDR 흰 섬광, 흰색 → 주황 → 빨강으로 식는 흑체색, 불빛이 깜빡이며 꺼짐, 재부팅이 '쪼'로 꽂힘.
    /// 교환 글리치(차가운 적 · 청 분리, 라벤더/라임 섬광)와 구분되도록 피해 글리치는 따뜻한 색으로 갈라진다.
    ///   약(Light)  : 모듈 전체가 충격에 밀렸다 '쪼'로 돌아오고 띠가 찢기며 깜빡인다. 맞은 모듈은 순간 하얗게 타고 잔상이 식는다.
    ///   중(Medium) : 맞은 모듈이 하얗게 탄 뒤 0.4초 동안 거칠게 찢기고 갈라지고(노이즈 블록), 신호가 깜빡이다 끊긴다 → 스캔하며 재부팅.
    ///   강(Heavy)  : 화면을 덮는 HDR 화이트 플래시(쿨다운 안에서는 약하게) → 모든 모듈이 함께 글리치 → 암전 → 모듈별로 차례로 재부팅.
    /// 파츠 교환 · 반환은 HudSwapGlitch가 맡는다 (스킬 모듈의 주인 바뀜도 여기서 그쪽으로 넘긴다).
    ///
    /// 부르는 법 (네트워크 · 피격 쪽):
    ///   HudBreak.Hit(lostHealth01)                       — 한 번에 잃은 최대 체력 비율로 세기를 고른다 (HudPlayerBridge가 부른다)
    ///   HudBreak.Play(HudModule.Vitals, HudBreakSeverity.Medium)
    ///   HudBreak.Play(HudModule.All, HudBreakSeverity.Heavy)
    ///
    /// 성능: 대기 중 비용 0 (LateUpdate는 재생 중인 모듈만 돈다). 고스트 · 노이즈 블록은 모듈마다 한 번 만들어 재사용하고
    /// 시작 직후 프레임마다 하나씩 미리 만든다. 연출은 중첩 Canvas 아래에 있다. 재생 중 GC 할당 없음.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudBreak : MonoBehaviour
    {
        /// <summary>마지막으로 켜진 HUD (한 화면에 하나).</summary>
        public static HudBreak Local { get; private set; }

        public static bool Play(HudModule module, HudBreakSeverity severity) => Local && Local.Break(module, severity);
        public static bool Play(int channel, HudBreakSeverity severity) => Play((HudModule)channel, severity);
        /// <summary>큰 피격. damage01 = 이번 피격으로 잃은 체력 / 최대 체력. 아무것도 안 했으면 null.</summary>
        public static HudBreakSeverity? Hit(float damage01) => Local ? Local.HitBy(damage01) : null;

        [SerializeField] OnYourMarkHud hud;
        [Tooltip("비워 두면 같은 오브젝트에서 찾는다. 스킬 모듈의 주인 바뀜을 여기로 넘긴다")]
        [SerializeField] HudSwapGlitch swapGlitch;
        [Tooltip("HudModule 순서: 0 생체, 1 지도, 2 소모품, 3 스킬")]
        [SerializeField] string[] channelModules = { "Vitals", "Spatial", "Reserves", HudSkillPanel.ModuleName };
        [Tooltip("Heavy · 흔들림에 함께 들어가는 나머지 모듈 (보이는 것만)")]
        [SerializeField] string[] otherModules = { "Reticle", "Objective", "Exchange", "Critical", "Awakened", "ToggleChannels", "ToggleSupport", "QuickShield", "QuickScan", "AllyMarker" };

        [Header("큰 피격 → 세기 (한 번에 잃은 최대 체력 비율)")]
        [SerializeField, Range(0f, 1f)] float lightAt = 0.08f;
        [SerializeField, Range(0f, 1f)] float mediumAt = 0.18f;
        [SerializeField, Range(0f, 1f)] float heavyAt = 0.32f;
        [Tooltip("Heavy 사이 최소 간격(초). 안에 또 오면 Medium으로 낮춘다")]
        [SerializeField, Min(0f)] float heavyCooldown = 4f;
        [Tooltip("화면을 덮는 HDR 플래시 최소 간격(초). 안에서는 약하게만 번쩍인다 (매번 덮으면 과하다)")]
        [SerializeField, Min(0f)] float screenFlashCooldown = 8f;

        [Header("모양")]
        [Tooltip("잔상 머티리얼 발광 (화면 상대 HDR): 흰 섬광 · 흑체색 잔상이 이만큼까지 빛난다 (11-19 눈부심: 8 → 4)")]
        [SerializeField, Range(2f, 16f)] float hotEmission = 4f;
        [Tooltip("화면 전체 플래시 발광 (11-19 \"섬광이 그대로야 눈이 너무 아파\": 4 → 1, HDR로 번지지 않게)")]
        [SerializeField, Range(1f, 12f)] float screenFlashEmission = 1f;
        [Tooltip("화면 전체 플래시의 최대 불투명도. 1이면 두 프레임 화이트아웃, 0.1이면 살짝 밝아지는 정도 (11-19 사용자: 눈 아픔)")]
        [SerializeField, Range(0f, 1f)] float screenFlashPeak = 0.1f;
        [Tooltip("찢김 세기 배수")]
        [SerializeField, Range(0f, 3f)] float intensity = 1f;
        [Tooltip("모듈당 노이즈 블록 수")]
        [SerializeField, Range(0, 16)] int noiseBlocks = 8;

        [Header("소리 (선택)")]
        [SerializeField] AudioSource sfxSource;
        [SerializeField] AudioClip glitchClip, heavyClip, rebootClip;

        [Header("시연")]
        [Tooltip("hud.demoInput일 때 F8 약 · F9 중(모듈 순환) · F10 강")]
        [SerializeField] bool demoKeys = true;

        /// <summary>(모듈, 세기) 고장 시작.</summary>
        public event Action<HudModule, HudBreakSeverity> Broken;

        public bool IsPlaying => _playing > 0;

        // ------------------------------------------------------------------------------------------------ timeline (s)
        const float LightTime = 0.5f;
        const float BootTime = 0.42f;
        const float BootOn = 0.035f, BootOff = 0.075f, BootScan = 0.17f;

        enum Kind { None, Light, Glitch }

        struct Block { public Image image; public float until; }

        sealed class Module
        {
            public string name;
            public int order;
            public RectTransform root;
            public CanvasGroup group;
            public Graphic[] graphics;
            public RectTransform[] rects;
            public HudBreakMesh[] liveFx;
            public readonly LiveBreak live = new LiveBreak();

            public RectTransform ghost;
            public CanvasGroup ghostGroup;
            public Graphic[] ghostGraphics;
            public RectTransform[] ghostRects;
            public HudBreakMesh[] ghostFx;
            public readonly GhostBreak after = new GhostBreak();
            public Block[] blocks;
            public bool built, ghostOn, rebooting;

            public Kind kind;
            public bool playing, impact, heavy;
            public float start, storm, lose, bootAt, endAt, immuneUntil, nextBlock, jolt;
            public Vector2 size, hitDir;
        }

        readonly List<Module> _modules = new List<Module>(16);
        RectTransform _fx;
        Image _screenFlash;
        Material _hotMat, _flashMat;
        HudSkillPanel _skills;
        int _playing, _prewarm, _demoChannel;
        bool _ready, _flashOn, _gammaVertex;
        float _flashStart = -99f, _flashGain, _lastHeavy = -99f, _lastScreenFlash = -99f;
        Color32 _white = new Color32(0xF3, 0xEE, 0xDD, 255);
        static readonly int EmissionId = Shader.PropertyToID("_Emission");
        static readonly int ScanId = Shader.PropertyToID("_ScanStrength");

        void Reset() => hud = GetComponent<OnYourMarkHud>();

        void Awake()
        {
            if (!hud) hud = GetComponent<OnYourMarkHud>();
            if (!swapGlitch) swapGlitch = GetComponent<HudSwapGlitch>();
        }

        void OnEnable()
        {
            Local = this;
            var skills = GetComponent<HudSkillPanel>();
            if (skills) Watch(skills);
        }

        void OnDisable()
        {
            if (Local == this) Local = null;
            if (_skills) _skills.OwnerSwapping -= OnSkillsSwapping;
            _skills = null;
            StopAll();
        }

        void OnDestroy()
        {
            if (_hotMat) Destroy(_hotMat);
            if (_flashMat) Destroy(_flashMat);
        }

        /// <summary>HudSkillPanel의 주인 바뀜(스킬 파츠 반환 · 리셋)을 듣는다. 두 컴포넌트 어느 쪽이 먼저 켜져도 한 번만 연결된다.</summary>
        public void Watch(HudSkillPanel panel)
        {
            if (!panel || _skills == panel) return;
            if (_skills) _skills.OwnerSwapping -= OnSkillsSwapping;
            _skills = panel;
            _skills.OwnerSwapping += OnSkillsSwapping;
        }

        // 스킬 파츠 교환 · 반환도 다른 채널처럼 교환 글리치로 (HudSwapGlitch 모듈 3 = Skills)
        void OnSkillsSwapping(bool toSelf) { if (swapGlitch) swapGlitch.Play((int)HudModule.Skills, toSelf); }

        // ------------------------------------------------------------------------------------------------ API

        public HudBreakSeverity? HitBy(float damage01)
        {
            if (damage01 < lightAt || !EnsureReady()) return null;
            float now = Time.unscaledTime;
            var sev = damage01 >= heavyAt ? HudBreakSeverity.Heavy : damage01 >= mediumAt ? HudBreakSeverity.Medium : HudBreakSeverity.Light;
            if (sev == HudBreakSeverity.Heavy && now - _lastHeavy < heavyCooldown) sev = HudBreakSeverity.Medium;
            if (sev == HudBreakSeverity.Medium)
            {
                int pick = PickChannel();
                if (pick < 0) sev = HudBreakSeverity.Light;
                else
                {
                    Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
                    for (int i = 0; i < _modules.Count; i++) if (i != pick) StartLight(_modules[i], false, dir);
                    StartGlitch(_modules[pick], false, dir);
                    Sound(glitchClip);
                    Broken?.Invoke((HudModule)pick, sev);
                    return sev;
                }
            }
            Break(HudModule.All, sev);
            return sev;
        }

        /// <summary>module = All이면 보이는 모든 모듈. Light(All)은 한 모듈만 하얗게 타고 나머지는 흔들린다.</summary>
        public bool Break(HudModule module, HudBreakSeverity severity)
        {
            if (!EnsureReady()) return false;
            Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
            if (module == HudModule.All)
            {
                switch (severity)
                {
                    case HudBreakSeverity.Light:
                        int impact = PickChannel();
                        for (int i = 0; i < _modules.Count; i++) StartLight(_modules[i], i == impact, dir);
                        break;
                    case HudBreakSeverity.Medium:
                        for (int i = 0; i < _modules.Count; i++) StartGlitch(_modules[i], false, dir);
                        break;
                    default:
                        _lastHeavy = Time.unscaledTime;
                        ScreenFlash();
                        for (int i = 0; i < _modules.Count; i++) StartGlitch(_modules[i], true, dir);
                        break;
                }
                Sound(severity == HudBreakSeverity.Heavy ? heavyClip : glitchClip);
                Broken?.Invoke(HudModule.All, severity);
                return true;
            }
            int index = (int)module;
            if (index < 0 || index >= channelModules.Length) return false;
            var m = _modules[index];
            bool ok = severity == HudBreakSeverity.Light ? StartLight(m, true, dir) : StartGlitch(m, severity == HudBreakSeverity.Heavy, dir);
            if (ok)
            {
                if (severity == HudBreakSeverity.Heavy) { _lastHeavy = Time.unscaledTime; ScreenFlash(); }
                Sound(severity == HudBreakSeverity.Heavy ? heavyClip : glitchClip);
                Broken?.Invoke(module, severity);
            }
            return ok;
        }

        public void StopAll()
        {
            for (int i = 0; i < _modules.Count; i++) if (_modules[i].playing) Finish(_modules[i]);
            if (_screenFlash) _screenFlash.enabled = false;
            _flashOn = false;
        }

        /// <summary>신호가 끊겼거나 재부팅 중인지.</summary>
        public bool IsDown(HudModule module)
        {
            int i = (int)module;
            return i >= 0 && i < _modules.Count && _modules[i].playing && _modules[i].kind == Kind.Glitch;
        }

        // ------------------------------------------------------------------------------------------------ start

        bool StartLight(Module m, bool impact, Vector2 dir)
        {
            if (!Prepare(m)) return false;
            if (m.playing && m.kind != Kind.Light) return false;   // 이미 신호가 끊긴 모듈은 흔들 것이 없다
            Begin(m, Kind.Light, false, dir);
            m.impact = impact;
            m.jolt = impact ? 11f : 6f;
            m.endAt = LightTime;
            m.live.active = true;
            Apply(m, 0f);
            return true;
        }

        bool StartGlitch(Module m, bool heavy, Vector2 dir)
        {
            if (!Prepare(m)) return false;
            if (m.playing && m.kind == Kind.Glitch) return false;
            if (Time.unscaledTime < m.immuneUntil && !heavy) return StartLight(m, true, dir);   // 막 재부팅한 모듈은 흔들림만
            Begin(m, Kind.Glitch, heavy, dir);
            m.impact = true;
            m.storm = heavy ? 0.3f : 0.4f;                // 거칠게 찢기는 구간
            m.lose = m.storm + (heavy ? 0.2f : 0.22f);    // 신호가 깜빡이다 끊기는 구간
            // 강: 조준점 → 생체 → 스킬 → 소모품 → 지도 → 나머지 순으로 재부팅 (암전이 길게 느껴지게)
            m.bootAt = heavy ? 1.0f + 0.06f * m.order : 0.82f;
            m.endAt = m.bootAt + BootTime;
            m.live.active = true;
            Apply(m, 0f);
            return true;
        }

        void Begin(Module m, Kind kind, bool heavy, Vector2 dir)
        {
            if (!m.playing) _playing++;
            m.playing = true;
            m.kind = kind;
            m.heavy = heavy;
            m.impact = false;
            m.rebooting = false;
            m.start = Time.unscaledTime;
            m.nextBlock = m.start;
            m.hitDir = dir == Vector2.zero ? Vector2.right : dir;
            m.size = m.root.rect.size;
            int seed = UnityEngine.Random.Range(1, int.MaxValue);
            m.live.seed = seed;
            m.after.seed = seed ^ 0x5bd1e995;
            m.after.gammaVertex = _gammaVertex;
            bool wasLive = m.live.active;
            m.live.active = false;
            m.live.Reset();
            if (wasLive) Dirty(m.graphics);
            CacheOffsets(m);
            SnapshotToGhost(m);
        }

        // ------------------------------------------------------------------------------------------------ update

        void LateUpdate()   // OnYourMarkHud.Update · HudSkillPanel이 값을 쓴 뒤
        {
            if (demoKeys && hud && hud.demoInput) DemoInput();
            if (!_ready && (!hud || !hud.isActiveAndEnabled || !EnsureReady())) return;
            if (_prewarm < _modules.Count) { Prepare(_modules[_prewarm]); _prewarm++; }   // 첫 고장에서 생성이 몰리지 않게 프레임마다 하나씩
            if (_playing == 0 && !_flashOn) return;

            float now = Time.unscaledTime;
            for (int i = 0; i < _modules.Count; i++)
            {
                var m = _modules[i];
                if (!m.playing) continue;
                float u = now - m.start;
                if (u >= m.endAt) { Finish(m); continue; }
                Apply(m, u);
                Blocks(m, u, now);
            }
            if (_flashOn) UpdateScreenFlash(now);
        }

        void Apply(Module m, float u)
        {
            bool still = hud && hud.reducedMotion;
            float amp = (still ? 0f : 1f) * intensity * (m.heavy ? 1.5f : 1f);
            var live = m.live;
            var g = m.after;
            int step = Mathf.FloorToInt(u * 30f);   // 찢김 패턴은 30 Hz로 바뀐다
            live.step = step;
            g.step = step;

            if (m.kind == Kind.Light)
            {
                float k = Mathf.Clamp01(u / 0.16f);
                live.offset = m.hitDir * (m.jolt * amp * (1f - Snap(k, 0.3f)));   // 밀렸다 '쪼'로 제자리
                float f = Mathf.Clamp01(1f - u / 0.38f);
                live.tear = (m.impact ? 12f : 6f) * amp * Mathf.Exp(-u / 0.07f);
                live.split = (m.impact ? 5f : 3f) * amp * Mathf.Exp(-u / 0.09f);
                live.splitHeat = Mathf.Exp(-u / 0.12f);
                live.dropout = 0.3f * f * f;
                live.alpha = HudGlitchMesh.Hash(live.seed, step, 999) < 0.3f * f ? 0.4f : 1f;   // 불빛이 꺼질 듯 깜빡
                live.flash = (m.impact ? 0.6f : 0.3f) * Mathf.Exp(-u / 0.05f);
                Dirty(m.graphics);

                bool on = m.impact && u < 0.2f;   // 맞은 모듈: 하얗게 탄 잔상이 옆으로 밀리며 식는다
                if (on)
                {
                    g.flash = Mathf.Exp(-u / 0.04f);
                    g.heat = Mathf.Exp(-u / 0.09f);
                    g.alpha = Mathf.Exp(-u / 0.07f);
                    g.tear = 10f * amp * Mathf.Exp(-u / 0.06f);
                    g.offset = live.offset + new Vector2((HudGlitchMesh.Hash(g.seed, step, 5) - 0.5f) * 8f * amp, 0f);
                    g.dropout = 0.3f;
                }
                SetGhost(m, on);
                if (on) Dirty(m.ghostGraphics);
                return;
            }

            // --- 중 · 강
            float b = u - m.bootAt;
            if (u < m.storm)
            {
                float s = u / m.storm;
                live.hidden = false;
                float jk = Mathf.Clamp01(u / 0.14f);
                float jitter = (HudGlitchMesh.Hash(live.seed, step, 11) - 0.5f) * 6f * amp * (1f - s);
                live.offset = m.hitDir * (14f * amp * (1f - Snap(jk, 0.3f))) + new Vector2(jitter, 0f);
                live.tear = (18f * (1f - s) + 6f) * amp * (HudGlitchMesh.Hash(live.seed, step, 13) < 0.7f ? 1f : 0.3f);
                live.split = 6f * amp * (1f - 0.5f * s);
                live.splitHeat = Mathf.Exp(-u / 0.2f);
                live.dropout = 0.12f + 0.35f * s;                                      // 신호가 점점 나빠진다
                live.alpha = HudGlitchMesh.Hash(live.seed, step, 999) < 0.2f + 0.3f * s ? 0.35f : 1f;
                live.flash = 0.7f * Mathf.Exp(-u / 0.05f);

                g.flash = Mathf.Exp(-u / 0.04f);
                g.heat = Mathf.Exp(-u / 0.22f);
                g.alpha = u < 0.05f ? 1f : 0.7f * (1f - s);
                g.tear = 16f * amp * (1f - s);
                g.offset = new Vector2((HudGlitchMesh.Hash(g.seed, step, 5) - 0.5f) * 16f * amp, 0f) + live.offset;
                g.dropout = 0.35f;
                SetGhost(m, true);
            }
            else if (u < m.lose)
            {
                // 신호 손실: 켜지는 프레임이 점점 드물어지다 꺼진다
                float l = (u - m.storm) / (m.lose - m.storm);
                bool on = HudGlitchMesh.Hash(live.seed, step, 77) > l * 1.1f;
                live.hidden = !on;
                live.offset = new Vector2((HudGlitchMesh.Hash(live.seed, step, 11) - 0.5f) * 8f * amp, 0f);
                live.alpha = 0.5f; live.dropout = 0.55f; live.tear = 10f * amp; live.split = 3f * amp; live.splitHeat = 0.3f; live.flash = 0f;

                g.flash = 0f;
                g.heat = 0.28f * (1f - l);              // 짙은 빨강으로 식어 가는 잔상
                g.alpha = 0.5f * (1f - l);
                g.tear = 8f * amp;
                g.dropout = 0.5f;
                SetGhost(m, g.alpha > 0.02f);
            }
            else if (b < 0f)
            {
                // 암전: 한 번만 숨기고 재부팅까지 다시 그리지 않는다
                SetGhost(m, false);
                if (!live.hidden) { live.hidden = true; Dirty(m.graphics); }
                return;
            }
            else Boot(m, b, still);

            Dirty(m.graphics);
            if (m.ghostOn) Dirty(m.ghostGraphics);
        }

        void Boot(Module m, float b, bool still)
        {
            var live = m.live;
            SetGhost(m, false);
            if (!m.rebooting) { m.rebooting = true; Sound(rebootClip); }
            live.hidden = false;
            live.offset = Vector2.zero;
            live.revealY = float.NegativeInfinity;
            live.scanGlow = 0f;
            live.flash = 0f;
            live.alpha = 1f;
            live.split = 0f;
            if (b < BootOn)
            {
                // 전원이 들어오다 한 번 꺼진다
                live.alpha = 0.55f; live.dropout = 0.45f; live.tear = still ? 0f : 8f;
            }
            else if (b < BootOff) live.hidden = true;
            else if (b < BootOff + BootScan)
            {
                // 위에서부터 스캔하며 켜진다
                float k = (b - BootOff) / BootScan;
                float e = 1f - (1f - k) * (1f - k) * (1f - k);
                live.revealY = -m.size.y * 1.15f * e + 2f;
                live.scanGlow = 1f;
                live.dropout = 0.15f * (1f - k);
                live.tear = still ? 0f : 3f * (1f - k);
            }
            else
            {
                // '쪼': 자리를 잡는 순간 흰 섬광, 끝에 한 번 더 깜빡
                float l = b - BootOff - BootScan;
                live.flash = 0.8f * Mathf.Exp(-l / 0.06f);
                live.dropout = 0f; live.tear = 0f;
                if (l > 0.07f && l < 0.095f) live.alpha = 0.5f;
            }
        }

        void Finish(Module m)
        {
            m.playing = false;
            _playing = Mathf.Max(0, _playing - 1);
            if (m.kind == Kind.Glitch) m.immuneUntil = Time.unscaledTime + 0.4f;
            m.kind = Kind.None;
            m.live.active = false;
            m.live.Reset();
            SetGhost(m, false);
            for (int i = 0; i < m.blocks.Length; i++) { m.blocks[i].until = 0f; if (m.blocks[i].image) m.blocks[i].image.enabled = false; }
            Dirty(m.graphics);   // 깨끗한 메쉬로 한 번 다시 그린다
        }

        // ------------------------------------------------------------------------------------------------ noise blocks

        void Blocks(Module m, float u, float now)
        {
            var blocks = m.blocks;
            for (int i = 0; i < blocks.Length; i++)
                if (blocks[i].image.enabled && now >= blocks[i].until) blocks[i].image.enabled = false;

            bool spawn = m.kind == Kind.Light ? m.impact && u < 0.18f : u < m.lose;
            if (!spawn || noiseBlocks <= 0 || (hud && hud.reducedMotion) || now < m.nextBlock) return;
            bool losing = m.kind == Kind.Glitch && u >= m.storm;
            m.nextBlock = now + (losing ? 0.05f : 0.022f);
            int free = -1;
            for (int i = 0; i < blocks.Length; i++) if (!blocks[i].image.enabled) { free = i; break; }
            if (free < 0) return;

            int salt = Mathf.FloorToInt(u * 1000f);
            float r1 = HudGlitchMesh.Hash(m.live.seed, salt, 1), r2 = HudGlitchMesh.Hash(m.live.seed, salt, 2), r3 = HudGlitchMesh.Hash(m.live.seed, salt, 3);
            float r4 = HudGlitchMesh.Hash(m.live.seed, salt, 4), r5 = HudGlitchMesh.Hash(m.live.seed, salt, 5), r6 = HudGlitchMesh.Hash(m.live.seed, salt, 6);
            var img = blocks[free].image;
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(Mathf.Lerp(8f, 90f, r1 * r2), Mathf.Lerp(2f, 7f, r3));
            rt.anchoredPosition = new Vector2(Mathf.Lerp(-0.1f, 1.05f, r4) * m.size.x, -r5 * m.size.y);
            // 뜨거운 흰색 · 주황 · 식은 빨강 · 꺼진 검정 (신호가 끊길수록 어둡다)
            float heat = losing ? 0.3f * r6 : r6;
            Color32 c = r6 < 0.12f ? new Color32(8, 7, 14, 255) : heat > 0.75f ? _white : (Color32)HudBreakMesh.Encode(HudBreakMesh.Blackbody(Mathf.Lerp(0.35f, 0.75f, heat)), 1f, _gammaVertex);
            c.a = (byte)Mathf.Lerp(90f, 220f, r2);
            img.color = c;
            img.enabled = true;
            blocks[free].until = now + Mathf.Lerp(0.03f, 0.085f, r3);
        }

        // ------------------------------------------------------------------------------------------------ screen flash

        void ScreenFlash()
        {
            float now = Time.unscaledTime;
            bool full = now - _lastScreenFlash >= screenFlashCooldown;
            if (full) _lastScreenFlash = now;
            _flashGain = (full ? 1f : 0.18f) * screenFlashPeak;
            _flashStart = now;
            _flashOn = true;
            if (_screenFlash) { _screenFlash.enabled = true; _screenFlash.transform.SetAsLastSibling(); }
            UpdateScreenFlash(now);
        }

        void UpdateScreenFlash(float now)
        {
            float t = now - _flashStart;
            // 약 0.1초 화이트아웃: 두 프레임 꽉 찬 뒤 3제곱으로 0까지 (지수 꼬리는 sRGB에서 회색 막으로 오래 남는다)
            float x = Mathf.Clamp01((t - 0.04f) / 0.07f);
            float a = t < 0.04f ? _flashGain : _flashGain * (1f - x) * (1f - x) * (1f - x);
            if (!_screenFlash || (t > 0.04f && a <= 0f)) { _flashOn = false; if (_screenFlash) _screenFlash.enabled = false; return; }
            _screenFlash.color = HudBreakMesh.Encode(new Color(1f, 0.95f, 0.88f), a, _gammaVertex);
        }

        // ------------------------------------------------------------------------------------------------ setup

        bool EnsureReady()
        {
            if (_ready) return true;
            if (!hud || !Application.isPlaying) return false;
            var canvas = hud.GetComponentInParent<Canvas>();
            var root = canvas ? canvas.rootCanvas : null;
            _gammaVertex = QualitySettings.activeColorSpace == ColorSpace.Linear && !(root && root.vertexColorAlwaysGammaSpace);
            if (hud.layoutAsset)
            {
                var colors = JsonUtility.FromJson<HudLayout>(hud.layoutAsset.text)?.colors;
                if (colors != null && ColorUtility.TryParseHtmlString(colors.white, out var w)) _white = w;
            }

            var go = new GameObject("Break FX", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
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

            if (hud.hologramMaterial)
            {
                _hotMat = new Material(hud.hologramMaterial) { name = "HUD Break Hot", hideFlags = HideFlags.DontSave };
                _hotMat.SetFloat(EmissionId, hotEmission);
                _flashMat = new Material(hud.hologramMaterial) { name = "HUD Break Flash", hideFlags = HideFlags.DontSave };
                _flashMat.SetFloat(EmissionId, screenFlashEmission);
                if (_flashMat.HasProperty(ScanId)) _flashMat.SetFloat(ScanId, 0f);
            }

            var fl = new GameObject("Screen flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fl.hideFlags = HideFlags.DontSave;
            fl.layer = go.layer;
            var frt = (RectTransform)fl.transform;
            frt.SetParent(_fx, false);
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            _screenFlash = fl.GetComponent<Image>();
            _screenFlash.raycastTarget = false;
            _screenFlash.material = _flashMat;
            _screenFlash.enabled = false;

            for (int i = 0; i < channelModules.Length; i++) _modules.Add(new Module { name = channelModules[i] });
            for (int i = 0; i < otherModules.Length; i++) _modules.Add(new Module { name = otherModules[i] });
            // 강한 고장 재부팅 순서: 조준점 → 생체 → 스킬 → 소모품 → 지도 → 나머지
            string[] bootOrder = { "Reticle", "Vitals", HudSkillPanel.ModuleName, "Reserves", "Spatial" };
            int order = 0;
            for (int b = 0; b < bootOrder.Length; b++)
                for (int i = 0; i < _modules.Count; i++) if (_modules[i].name == bootOrder[b]) _modules[i].order = order++;
            for (int i = 0; i < _modules.Count; i++) if (Array.IndexOf(bootOrder, _modules[i].name) < 0) _modules[i].order = order++;
            _ready = true;
            return true;
        }

        /// <summary>모듈을 찾고(없으면 다음에 다시) 처음이면 고스트 · 노이즈 블록을 만든다. 지금 보이지 않으면 false.</summary>
        bool Prepare(Module m)
        {
            if (!m.built)
            {
                m.root = hud.transform.Find(m.name) as RectTransform;
                if (!m.root) return false;
                BuildModule(m);
            }
            if (!m.root || !m.root.gameObject.activeInHierarchy) return false;
            if (m.group && m.group.alpha < 0.05f) return false;
            return true;
        }

        void BuildModule(Module m)
        {
            m.group = m.root.GetComponent<CanvasGroup>();
            m.graphics = m.root.GetComponentsInChildren<Graphic>(true);
            m.rects = new RectTransform[m.graphics.Length];
            m.liveFx = new HudBreakMesh[m.graphics.Length];
            for (int i = 0; i < m.graphics.Length; i++)
            {
                m.rects[i] = m.graphics[i].rectTransform;
                var fx = m.graphics[i].GetComponent<HudBreakMesh>();
                if (!fx) fx = m.graphics[i].gameObject.AddComponent<HudBreakMesh>();
                fx.hideFlags = HideFlags.DontSave;
                fx.Live = m.live;
                fx.Ghost = null;
                m.liveFx[i] = fx;
            }

            // 고스트: 모듈을 한 번 복제해 재사용한다. 머티리얼을 뜨거운(HDR) 것으로 바꿔 섬광 · 잔상만 그린다.
            var copy = Instantiate(m.root.gameObject, _fx, false);
            copy.name = m.root.name + " (break ghost)";
            copy.hideFlags = HideFlags.DontSave;
            m.ghost = (RectTransform)copy.transform;
            m.ghostGroup = copy.GetComponent<CanvasGroup>();
            if (m.ghostGroup) { m.ghostGroup.interactable = false; m.ghostGroup.blocksRaycasts = false; }
            foreach (var b in copy.GetComponentsInChildren<Selectable>(true)) Destroy(b);
            m.ghostGraphics = copy.GetComponentsInChildren<Graphic>(true);
            m.ghostRects = new RectTransform[m.ghostGraphics.Length];
            m.ghostFx = new HudBreakMesh[m.ghostGraphics.Length];
            for (int i = 0; i < m.ghostGraphics.Length; i++)
            {
                var gg = m.ghostGraphics[i];
                gg.raycastTarget = false;
                m.ghostRects[i] = gg.rectTransform;
                var fx = gg.GetComponent<HudBreakMesh>();
                if (!fx) fx = gg.gameObject.AddComponent<HudBreakMesh>();
                fx.hideFlags = HideFlags.DontSave;
                fx.Live = null;
                fx.Ghost = m.after;
                if (_hotMat && !(gg is RawImage)) gg.material = _hotMat;
                m.ghostFx[i] = fx;
            }

            // 노이즈 블록: 고스트 루트(모듈 공간 · 같은 3D 기울기) 아래에 몇 개를 만들어 재사용한다
            m.blocks = new Block[Mathf.Max(1, noiseBlocks)];
            for (int i = 0; i < m.blocks.Length; i++)
            {
                var bgo = new GameObject("glitch block", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                bgo.hideFlags = HideFlags.DontSave;
                bgo.layer = _fx.gameObject.layer;
                var rt = (RectTransform)bgo.transform;
                rt.SetParent(m.ghost, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                var img = bgo.GetComponent<Image>();
                img.raycastTarget = false;
                img.material = hud.hologramMaterial;
                img.enabled = false;
                m.blocks[i].image = img;
            }
            // 고스트는 켜 둔 채 재사용한다: 잔상이 꺼져 있으면 빈 메쉬만 내므로 그리는 비용이 없고,
            // 켜고 끌 때 OnEnable(머티리얼 · 레이아웃 · 캔버스 재등록)이 수백 개씩 몰리지 않는다
            m.ghostOn = false;
            m.after.active = false;
            if (_screenFlash) _screenFlash.transform.SetAsLastSibling();
            m.built = true;
        }

        void CacheOffsets(Module m)
        {
            for (int i = 0; i < m.liveFx.Length; i++)
            {
                Vector3 local = m.root.InverseTransformPoint(m.rects[i].TransformPoint(Vector3.zero));
                m.liveFx[i].ToModule = local;
                if (i < m.ghostFx.Length) m.ghostFx[i].ToModule = local;
            }
        }

        /// <summary>원본 모듈의 '지금' 모습을 고스트에 옮긴다. HudPerspective의 3D 기울기까지 그대로 겹친다.</summary>
        void SnapshotToGhost(Module m)
        {
            var g = m.ghost;
            g.anchoredPosition3D = m.root.anchoredPosition3D;
            g.localRotation = m.root.localRotation;
            g.sizeDelta = m.root.sizeDelta;
            g.localScale = m.root.localScale;
            if (m.ghostGroup) m.ghostGroup.alpha = m.group ? m.group.alpha : 1f;
            int n = Mathf.Min(m.graphics.Length, m.ghostGraphics.Length);
            for (int i = 0; i < n; i++)
            {
                Graphic src = m.graphics[i], dst = m.ghostGraphics[i];
                if (!src || !dst) continue;
                if (dst.gameObject.activeSelf != src.gameObject.activeSelf) dst.gameObject.SetActive(src.gameObject.activeSelf);
                if (dst.enabled != src.enabled) dst.enabled = src.enabled;   // 꺼진 탄약 칸 등 (드물게 바뀐다)
                dst.color = src.color;
                m.ghostRects[i].anchoredPosition = m.rects[i].anchoredPosition;
                m.ghostRects[i].sizeDelta = m.rects[i].sizeDelta;
                if (src is Text st && dst is Text dt) { if (!ReferenceEquals(dt.text, st.text)) dt.text = st.text; }
                else if (src is HudGeometry sg && dst is HudGeometry dg && sg.iconName != dg.iconName)
                {
                    dg.iconName = sg.iconName; dg.strokes = sg.strokes; dg.iconWidth = sg.iconWidth; dg.iconHeight = sg.iconHeight;
                    dg.Refresh();
                }
                else if (src is RawImage sr && dst is RawImage dr) { dr.texture = sr.texture; dr.uvRect = sr.uvRect; }
            }
        }

        void SetGhost(Module m, bool on)
        {
            if (m.ghostOn == on || m.ghostGraphics == null) return;
            m.ghostOn = on;
            m.after.active = on;
            if (!on) Dirty(m.ghostGraphics);   // 빈 메쉬로 한 번 (켤 때는 Apply가 매 프레임 갱신한다)
        }

        int PickChannel()
        {
            int start = UnityEngine.Random.Range(0, channelModules.Length);
            for (int k = 0; k < channelModules.Length; k++)
            {
                int i = (start + k) % channelModules.Length;
                var m = _modules[i];
                if (Prepare(m) && !(m.playing && m.kind == Kind.Glitch)) return i;
            }
            return -1;
        }

        void Sound(AudioClip clip)
        {
            if (sfxSource && clip) sfxSource.PlayOneShot(clip);
        }

        void DemoInput()
        {
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k == null) return;
            if (k.f8Key.wasPressedThisFrame) Break(HudModule.All, HudBreakSeverity.Light);
            if (k.f9Key.wasPressedThisFrame && EnsureReady())
            {
                int c = _demoChannel++ % channelModules.Length;
                Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
                for (int i = 0; i < _modules.Count; i++) if (i != c) StartLight(_modules[i], false, dir);
                Break((HudModule)c, HudBreakSeverity.Medium);
            }
            if (k.f10Key.wasPressedThisFrame) Break(HudModule.All, HudBreakSeverity.Heavy);
        }

        static void Dirty(Graphic[] graphics)
        {
            if (graphics == null) return;
            for (int i = 0; i < graphics.Length; i++) if (graphics[i] && graphics[i].isActiveAndEnabled) graphics[i].SetVerticesDirty();
        }

        /// <summary>'쪼': 72%까지 cubic-out으로 1+ov까지 갔다가, 남은 구간에 smoothstep으로 1로 돌아와 멈춘다 (HudSwapGlitch와 같은 곡선).</summary>
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
