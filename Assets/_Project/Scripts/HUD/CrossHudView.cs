using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>
    /// S2 인게임 HUD. 파츠(생체 / 지도 / 소모품)마다 반환 전이면 상대, 반환 후면 내 정보를 보여 준다.
    /// 반환 전 파츠에는 그 파츠로 줄 수 있는 지원의 쿨타임을 함께 표시한다.
    /// 대쉬 · 호버 · 웨이브 · 내 요청 · 받은 효과는 항상 내 정보다.
    /// </summary>
    public class CrossHudView : MonoBehaviour
    {
        [SerializeField] CrossHudState state;
        [SerializeField] PartExchange exchange;
        [SerializeField] WaveManager waves;
        [SerializeField] UnlockTerminal terminal;
        [SerializeField] GameObject root;

        [Header("1 생체")]
        [SerializeField] TMP_Text bioLabel;
        [SerializeField] Image bioFill;

        [Header("2 지도")]
        [SerializeField] TMP_Text mapLabel;
        [SerializeField] Camera mapCamera;
        [SerializeField] float mapCameraHeight = 30f;

        [Header("3 소모품")]
        [SerializeField] TMP_Text ammoLabel;
        [SerializeField] Image ammoFill;

        [Header("내 정보")]
        [SerializeField] TMP_Text dashLabel;
        [SerializeField] Image hoverFill;
        [SerializeField] TMP_Text waveLabel;
        [SerializeField] TMP_Text partsLabel;
        [SerializeField] TMP_Text statusLabel;

        [Header("교환 / 단말기 안내")]
        [SerializeField] GameObject prompt;
        [SerializeField] TMP_Text promptLabel;
        [SerializeField] Image promptFill;

        [Header("요청 휠 (Q 홀드)")]
        [SerializeField] GameObject wheel;
        [SerializeField] TMP_Text[] wheelOptions;   // HudPart 순서

        Player me, partner;

        void Update()
        {
            me = Player.Local;
            partner = me ? me.Partner : null;
            root.SetActive(me != null);
            if (me == null) return;

            ShowBio(Subject(HudPart.Bio));
            ShowMap(Subject(HudPart.Map));
            ShowConsumable(Subject(HudPart.Consumable));
            ShowSelf();
            ShowWheel();
            ShowPrompt();
        }

        /// <summary>그 파츠로 누구의 정보를 보는지. 상대가 아직 없으면 비워 둔다.</summary>
        Player Subject(HudPart part) => state.IsReturned(part) ? me : partner;

        void ShowBio(Player who)
        {
            string shield = who && who.Health.Shield > 0 ? $"  <color=#8FD3FF>+{who.Health.Shield:F0} SHIELD</color>" : "";
            bioLabel.text = $"1 BIO  {Name(who)}{shield}{Cooldown(HudPart.Bio)}";
            bioFill.fillAmount = who ? who.Health.Ratio : 0f;
        }

        void ShowMap(Player who)
        {
            mapLabel.text = $"2 MAP  {Name(who)}{Cooldown(HudPart.Map)}";
            mapCamera.enabled = who != null;
            if (who) mapCamera.transform.position = who.transform.position + Vector3.up * mapCameraHeight;
        }

        void ShowConsumable(Player who)
        {
            string count = who ? $"  {who.Ammo.Current}/{who.Ammo.Capacity}" : "";
            ammoLabel.text = $"3 AMMO  {Name(who)}{count}{Cooldown(HudPart.Consumable)}";
            ammoFill.fillAmount = who ? who.Ammo.Ratio : 0f;
        }

        void ShowSelf()
        {
            dashLabel.text = $"DASH {me.Motor.DashStacks}/{me.Stats.dashMaxStacks}";
            hoverFill.fillAmount = me.Motor.HoverLeft01;

            waveLabel.text =
                waves.WaveNumber == 0 ? "GET READY" :
                waves.AllCleared ? "ALL WAVES CLEAR" :
                waves.BetweenWaves ? $"WAVE {waves.WaveNumber} CLEAR   NEXT IN {waves.SecondsToNextWave:F0}s{(terminal.IsAvailable ? "   <color=#F08045>TERMINAL OPEN</color>" : "")}" :
                $"WAVE {waves.WaveNumber}/{waves.TotalWaves}   ENEMIES {waves.Alive}";

            partsLabel.text = $"{Key(HudPart.Bio, "1")}  {Key(HudPart.Map, "2")}  {Key(HudPart.Consumable, "3")}  SWAP";

            string status = "";
            if (me.Requests.Any) status += "<color=#FFD166>REQUESTING</color>  ";
            if (me.Buffs.SpeedMultiplier > 1f) status += "<color=#8FD3FF>RAIL BOOST</color>  ";
            if (me.Buffs.RevealsWeakPoints) status += "<color=#FF6B6B>WEAK POINTS</color>";
            statusLabel.text = status;
        }

        void ShowWheel()
        {
            wheel.SetActive(me.Requests.WheelOpen);
            for (int i = 0; i < wheelOptions.Length; i++)
                wheelOptions[i].color = me.Requests.WheelChoice == (HudPart)i ? new Color(1f, 0.82f, 0.4f) : Color.white;
        }

        // S4 교환 제안 / S5 단말기 해금 진행
        void ShowPrompt()
        {
            string text = null;
            float fill = 0f;

            if (exchange.HasOffer)
            {
                string key = $"[{(int)exchange.OfferPart + 1}] {PartName(exchange.OfferPart)}";
                text = exchange.OfferFrom == me.OwnerClientId
                    ? $"SWAP {key}: WAITING FOR PARTNER"
                    : $"PARTNER WANTS TO SWAP {key}: PRESS {(int)exchange.OfferPart + 1}";
                fill = exchange.TimeLeft01;
            }
            else if (terminal.IsAvailable && terminal.IsInside(me))
            {
                text = terminal.Progress01 > 0f ? "UNLOCKING..." : "HOLD E TOGETHER TO UNLOCK";
                fill = terminal.Progress01;
            }

            prompt.SetActive(text != null);
            if (text == null) return;
            promptLabel.text = text;
            promptFill.fillAmount = fill;
        }

        static string PartName(HudPart part) => part switch
        {
            HudPart.Bio => "BIO",
            HudPart.Map => "MAP",
            _ => "AMMO",
        };

        // 반환 전 파츠: 이 파츠로 상대를 지원할 수 있으니 쿨타임을 보여 준다
        string Cooldown(HudPart part)
        {
            if (state.IsReturned(part) || partner == null) return "";
            float left = me.GetComponent<SupportGiver>().CooldownLeft(part);
            return left > 0f ? $"  <color=#9AA5B1>CD {left:F1}s</color>" : "  <color=#7CFC9A>READY</color>";
        }

        string Key(HudPart part, string key) => state.IsReturned(part) ? $"<color=#7CFC9A>[{key}]</color>" : $"[{key}]";

        static string Name(Player who) =>
            who ? $"<color=#{ColorUtility.ToHtmlStringRGB(who.Stats.color)}>{who.Stats.displayName}</color>" : "-";
    }
}
