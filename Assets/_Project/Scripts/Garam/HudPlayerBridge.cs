using OnYourMark.UI;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// main의 SenseLink HUD를 프로토타입의 교차 정보·교환·지원 요청에 연결한다.
    /// 검은 동기화된 대쉬 횟수, 레일건은 실제 잔탄 수를 표시한다.
    /// </summary>
    [DefaultExecutionOrder(-5)]
    public class HudPlayerBridge : MonoBehaviour
    {
        [SerializeField] OnYourMarkHud hud;
        [SerializeField] CrossHudState state;
        [SerializeField] PartExchange exchange;
        [SerializeField] UnityEngine.UI.RawImage mapImage;

        Canvas canvas;
        UnityEngine.UI.GraphicRaycaster raycaster;

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            raycaster = GetComponent<UnityEngine.UI.GraphicRaycaster>();
            hud.demoInput = false;
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
            hud.localHealth = me.Health.Ratio;
            hud.localAmmo = ResourceCount(me);
            hud.localAmmoCapacity = ResourceCapacity(me);
            hud.localResourceIsDash = me.Stats.showDashOnHud;
            hud.SetPartnerTelemetry(partner ? partner.Health.Ratio : 1f,
                ResourceCount(partner), ResourceCapacity(partner), partner && partner.Stats.showDashOnHud);
            hud.SetGameState(state.IsReturned(HudPart.Bio), state.IsReturned(HudPart.Map),
                state.IsReturned(HudPart.Consumable), exchange.HasOffer ? (int)exchange.OfferPart : -1);
            mapImage.enabled = state.IsReturned(HudPart.Map) || partner != null;

            if (CursorLock.IsLocked) hud.openPanel = OnYourMarkHud.Panel.Closed;
        }

        static int ResourceCount(Player player) => !player ? 0
            : player.Stats.showDashOnHud ? player.DashStacks : player.Ammo.Current;

        static int ResourceCapacity(Player player) => !player ? 0
            : player.Stats.showDashOnHud ? player.DashCapacity : player.Ammo.Capacity;

        void RequestExchange(int channel) => exchange.Offer((HudPart)channel);
        void CancelExchange() => exchange.CancelOffer();
    }
}
