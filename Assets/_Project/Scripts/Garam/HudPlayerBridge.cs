using OnYourMark.UI;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// main의 SenseLink HUD를 프로토타입의 교차 정보·교환·지원 요청에 연결한다.
    /// 검은 동기화된 대쉬 횟수, 레일건은 실제 잔탄 수를 표시한다.
    /// 파트너가 처음 잡히는 순간(서로의 HUD를 들고 시작) HudSwapGlitch 크로스 링크를 재생한다.
    /// 파츠 교환 · 반환은 OnYourMarkHud.SetGameState → ChannelSwapping으로 글리치가 자동 재생된다.
    /// 3번 파츠(스킬): 반환 전에는 Q가 파트너의 궁극기를 발동하므로 파트너 유닛의 Q · E를, 반환 후에는 내 것을 HudSkillPanel에 보여 준다.
    /// 큰 피격: 한 번에 잃은 체력 비율로 HudBreak 고장 세기(약 · 중 · 강)를 고른다.
    /// </summary>
    [DefaultExecutionOrder(-5)]
    public class HudPlayerBridge : MonoBehaviour
    {
        [SerializeField] OnYourMarkHud hud;
        [SerializeField] CrossHudState state;
        [SerializeField] PartExchange exchange;
        [SerializeField] UnityEngine.UI.RawImage mapImage;
        [Tooltip("비워 두면 HUD 오브젝트에서 찾는다")]
        [SerializeField] HudSwapGlitch swapGlitch;
        [Tooltip("비워 두면 HUD 오브젝트에서 찾고, 없으면 붙인다")]
        [SerializeField] HudSkillPanel skillPanel;
        [Tooltip("비워 두면 HUD 오브젝트에서 찾고, 없으면 붙인다")]
        [SerializeField] HudBreak hudBreak;

        Canvas canvas;
        UnityEngine.UI.GraphicRaycaster raycaster;
        Player linkedPartner;
        Player hitWatch;
        float lastHealth;

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            raycaster = GetComponent<UnityEngine.UI.GraphicRaycaster>();
            hud.demoInput = false;
            // The arena's LockOnReticle draws both the idle sight and the target marker.
            var previewReticle = hud.transform.Find("Reticle");
            if (previewReticle) previewReticle.gameObject.SetActive(false);
            if (!swapGlitch) swapGlitch = hud.GetComponent<HudSwapGlitch>();
            if (!skillPanel) skillPanel = hud.GetComponent<HudSkillPanel>();
            if (!skillPanel) skillPanel = hud.gameObject.AddComponent<HudSkillPanel>();
            if (!hudBreak) hudBreak = hud.GetComponent<HudBreak>();
            if (!hudBreak) hudBreak = hud.gameObject.AddComponent<HudBreak>();
            if (!hud.GetComponent<HudArrangement>()) hud.gameObject.AddComponent<HudArrangement>();
            if (!hud.GetComponent<HudPerspective>()) hud.gameObject.AddComponent<HudPerspective>();
        }

        void OnEnable()
        {
            hud.ExchangeRequested += RequestExchange;
            hud.ExchangeCancelled += CancelExchange;
        }

        void OnDisable()
        {
            hud.ExchangeRequested -= RequestExchange;
            hud.ExchangeCancelled -= CancelExchange;
        }

        void Update()
        {
            var me = Player.Local;
            canvas.enabled = me != null;
            raycaster.enabled = me != null && !CursorLock.IsLocked;
            if (me == null)
            {
                hud.openPanel = OnYourMarkHud.Panel.Closed;
                return;
            }

            var partner = me.Partner;
            if (partner != linkedPartner)
            {
                linkedPartner = partner;
                if (partner && swapGlitch && !state.IsReturned(HudPart.Vitals)) swapGlitch.PlayCrossLink(toSelf: false);   // 이제 서로의 감각을 본다
            }
            hud.localHealth = me.Health.Ratio;
            hud.localAmmo = ResourceCount(me);
            hud.localAmmoCapacity = ResourceCapacity(me);
            hud.localResourceIsDash = me.Stats.showDashOnHud;
            hud.SetPartnerTelemetry(partner ? partner.Health.Ratio : 1f,
                ResourceCount(partner), ResourceCapacity(partner), partner && partner.Stats.showDashOnHud);
            // SenseLink 화면: 체력 · 지도는 1번 파츠, 잔탄 · 대쉬는 2번 파츠를 따른다 (3번 스킬은 아래 HudSkillPanel)
            hud.SetGameState(state.IsReturned(HudPart.Vitals), state.IsReturned(HudPart.Vitals),
                state.IsReturned(HudPart.Resource), exchange.HasOffer ? (int)exchange.OfferPart : -1);
            mapImage.enabled = state.IsReturned(HudPart.Vitals) || partner != null;

            // 3번 파츠: 반환 전에는 파트너의 스킬 (내 Q가 파트너의 궁극기를 발동한다)
            bool ownSkill = state.IsReturned(HudPart.Skill);
            if (skillPanel) skillPanel.SetTelemetry(SkillOf(ownSkill ? me : partner), ownSkill);

            // 큰 피격 → HUD 고장. 회복 · 소생 · 라운드 리셋(체력 증가)은 무시한다
            float hp = me.Health.Current;
            if (me == hitWatch && hp < lastHealth && hudBreak)
                hudBreak.HitBy((lastHealth - hp) / Mathf.Max(1f, me.Health.Max));
            hitWatch = me;
            lastHealth = hp;

            if (CursorLock.IsLocked) hud.openPanel = OnYourMarkHud.Panel.Closed;
        }

        static int ResourceCount(Player player) => !player ? 0
            : player.Stats.showDashOnHud ? player.DashStacks : player.Ammo.Current;

        static int ResourceCapacity(Player player) => !player ? 0
            : player.Stats.showDashOnHud ? player.DashCapacity : player.Ammo.Capacity;

        static HudSkillPanel.Telemetry SkillOf(Player who)
        {
            var t = new HudSkillPanel.Telemetry();
            if (!who) return t;
            t.valid = true;
            var u = who.Ultimate;
            if (u)
            {
                t.kills = u.Kills;
                t.killsRequired = u.KillsRequired;
                t.ultReady = u.IsReady;
                t.ultActive = u.IsActive;
                t.ultLeft = u.ActiveLeft;
                t.ultDuration = u.Data ? u.Data.duration : 8f;
            }
            if (who.TryGetComponent<Parry>(out var parry))
            {
                t.eSkill = HudSkillPanel.ESkill.Parry;
                t.eActive = parry.IsActive;
                t.eCooldownLeft = parry.CooldownLeft;
                t.eCooldown = parry.Cooldown;
            }
            else if (who.TryGetComponent<WeaponModes>(out var modes))
            {
                t.eSkill = HudSkillPanel.ESkill.WeaponSwitch;
                t.eActive = modes.IsSwitching;
                t.eCooldownLeft = modes.CooldownLeft;
                t.eCooldown = modes.Data ? modes.Data.cooldown : 2f;
            }
            return t;
        }

        void RequestExchange(int channel) => exchange.Offer((HudPart)channel);
        void CancelExchange() => exchange.CancelOffer();
    }
}
