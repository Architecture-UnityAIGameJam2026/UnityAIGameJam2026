using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>
    /// S2 인게임 HUD. 파츠(생체 / 지도 / 소모품)마다 반환 전이면 상대, 반환 후면 내 정보를 보여 준다.
    /// 반환 전 파츠에는 그 파츠로 줄 수 있는 지원의 쿨타임을 함께 표시한다.
    /// 소모품은 검 캐릭터의 대쉬 / 레일건 캐릭터의 탄창을 표시한다.
    /// 호버 · 웨이브 · 받은 효과는 항상 내 정보다. 체력 지원 쿨타임은 1번 파츠에 표시한다.
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
        [Tooltip("기존 대쉬 위치에 표시하는 소모품 요약 (패널과 같은 대상)")]
        [SerializeField] TMP_Text dashLabel;

        [Header("내 정보")]
        [SerializeField] Image hoverFill;
        [SerializeField] TMP_Text waveLabel;
        [SerializeField] TMP_Text partsLabel;
        [SerializeField] TMP_Text statusLabel;

        [Header("교환 / 단말기 안내")]
        [SerializeField] GameObject prompt;
        [SerializeField] TMP_Text promptLabel;
        [SerializeField] Image promptFill;

        [Header("빈사")]
        [Tooltip("HP 0일 때 켜는 붉은 비네팅 (내 화면만)")]
        [SerializeField] Volume downedVolume;

        Player me, partner;

        void Update()
        {
            me = Player.Local;
            partner = me ? me.Partner : null;
            root.SetActive(me != null);
            float vignette = me != null && me.Health.IsDead ? 1f : 0f;
            downedVolume.weight = Mathf.MoveTowards(downedVolume.weight, vignette, Time.deltaTime * 3f);
            if (me == null) return;

            ShowBio(Subject(HudPart.Bio));
            ShowMap(Subject(HudPart.Map));
            ShowConsumable(Subject(HudPart.Consumable));
            ShowSelf();
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
            mapLabel.text = $"2 MAP  {Name(who)}";
            mapCamera.enabled = who != null;
            if (who) mapCamera.transform.position = who.transform.position + Vector3.up * mapCameraHeight;
        }

        void ShowConsumable(Player who)
        {
            if (who == null)
            {
                ammoLabel.text = "3 RESOURCE  -";
                ammoFill.fillAmount = 0f;
                dashLabel.text = "";
                return;
            }

            bool showDash = who.Stats.showDashOnHud;
            string resource = showDash ? "DASH" : "AMMO";
            int current = showDash ? who.DashStacks : who.Ammo.Current;
            int capacity = showDash ? who.Stats.dashMaxStacks : who.Ammo.Capacity;
            ammoLabel.text = $"3 {resource}  {Name(who)}  {current}/{capacity}";
            ammoFill.fillAmount = capacity > 0 ? (float)current / capacity : 0f;
            dashLabel.text = $"{resource} {current}/{capacity}";
        }

        void ShowSelf()
        {
            hoverFill.fillAmount = me.Motor.HoverLeft01;

            waveLabel.text =
                waves.WaveNumber == 0 ? "GET READY" :
                waves.AllCleared ? "ALL WAVES CLEAR" :
                waves.BetweenWaves ? $"WAVE {waves.WaveNumber} CLEAR   NEXT IN {waves.SecondsToNextWave:F0}s{(terminal.IsAvailable ? "   <color=#F08045>TERMINAL OPEN</color>" : "")}" :
                $"WAVE {waves.WaveNumber}/{waves.TotalWaves}   ENEMIES {waves.Alive}";

            partsLabel.text = $"{Key(HudPart.Bio, "1")}  {Key(HudPart.Map, "2")}  {Key(HudPart.Consumable, "3")}  SWAP";

            string status = "";
            if (me.Buffs.SpeedMultiplier > 1f) status += "<color=#8FD3FF>RAIL BOOST</color>  ";
            if (me.Buffs.RevealsWeakPoints) status += "<color=#FF6B6B>WEAK POINTS</color>  ";
            foreach (var a in me.GetComponent<AwakeningSet>().Active)
                status += $"<color=#C792EA>{a.displayName}</color>  ";
            statusLabel.text = status;
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
            else if (me.Health.IsDead)
            {
                float revive = partner ? partner.GetComponent<Reviver>().Progress01 : 0f;
                text = revive > 0f ? "<color=#7CFC9A>BEING REVIVED...</color>" : "<color=#FF6B6B>DOWNED</color>: WAIT FOR YOUR PARTNER";
                fill = revive;
            }
            else if (me.GetComponent<Reviver>().CanRevive(partner))
            {
                float revive = me.GetComponent<Reviver>().Progress01;
                text = revive > 0f ? "REVIVING..." : "HOLD F TO REVIVE PARTNER";
                fill = revive;
            }
            else if (terminal.IsAvailable && terminal.IsInside(me))
            {
                text = terminal.Progress01 > 0f ? "UNLOCKING..." : "HOLD F TOGETHER TO UNLOCK";
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
            _ => "RESOURCE",
        };

        // 반환 전 1번 파츠: 파트너를 조준해 회복시켜 줄 수 있으니 쿨타임을 보여 준다
        string Cooldown(HudPart part)
        {
            if (state.IsReturned(part) || partner == null) return "";
            float left = me.GetComponent<SupportGiver>().CooldownLeft;
            return left > 0f ? $"  <color=#9AA5B1>HEAL {left:F1}s</color>" : "  <color=#7CFC9A>HEAL READY</color>";
        }

        string Key(HudPart part, string key) => state.IsReturned(part) ? $"<color=#7CFC9A>[{key}]</color>" : $"[{key}]";

        static string Name(Player who) =>
            who ? $"<color=#{ColorUtility.ToHtmlStringRGB(who.Stats.color)}>{who.Stats.displayName}</color>" : "-";
    }
}
