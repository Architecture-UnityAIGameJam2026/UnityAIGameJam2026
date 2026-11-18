using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>
    /// S2 인게임 HUD (새 기획서 파츠 구조). 파츠마다 반환 전이면 상대, 반환 후면 내 정보를 보여 준다.
    /// 1 체력바 + 미니맵 (반환 전에는 파트너 회복 쿨타임 함께 표시)
    /// 2 탄창 · 대쉬 수 (검 캐릭터는 대쉬, 총 캐릭터는 탄창)
    /// 3 스킬: Q 킬 게이지와 E 쿨타임
    /// 호버 · 웨이브 · 받은 효과는 항상 내 정보다. 체력 지원 쿨타임은 1번 파츠에 표시한다.
    /// </summary>
    public class CrossHudView : MonoBehaviour
    {
        [SerializeField] CrossHudState state;
        [SerializeField] PartExchange exchange;
        [SerializeField] WaveManager waves;
        [SerializeField] UnlockTerminal terminal;
        [SerializeField] GameObject root;

        [Header("1 체력바")]
        [SerializeField] TMP_Text bioLabel;
        [SerializeField] Image bioFill;

        [Header("1 미니맵")]
        [SerializeField] TMP_Text mapLabel;
        [SerializeField] Camera mapCamera;
        [SerializeField] float mapCameraHeight = 30f;

        [Header("2 탄창 · 대쉬 수")]
        [SerializeField] TMP_Text ammoLabel;
        [SerializeField] Image ammoFill;

        [Header("3 스킬 (Q · E)")]
        [Tooltip("Q 킬 게이지 · E 쿨타임 (3번 파츠 대상)")]
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

            ShowBio(Subject(HudPart.Vitals));
            ShowMap(Subject(HudPart.Vitals));
            ShowResource(Subject(HudPart.Resource));
            ShowSkill(Subject(HudPart.Skill));
            ShowSelf();
            ShowPrompt();
        }

        /// <summary>그 파츠로 누구의 정보를 보는지. 상대가 아직 없으면 비워 둔다.</summary>
        Player Subject(HudPart part) => state.IsReturned(part) ? me : partner;

        void ShowBio(Player who)
        {
            string shield = who && who.Health.Shield > 0 ? $"  <color=#8FD3FF>+{who.Health.Shield:F0} SHIELD</color>" : "";
            string hp = who ? $"  {who.Health.Current:F0}/{who.Health.Max:F0}" : "";
            bioLabel.text = $"1 VITALS  {Name(who)}{hp}{shield}{Cooldown(HudPart.Vitals)}";
            bioFill.fillAmount = who ? who.Health.Ratio : 0f;
        }

        void ShowMap(Player who)
        {
            mapLabel.text = $"1 MAP  {Name(who)}";
            mapCamera.enabled = who != null;
            if (who) mapCamera.transform.position = who.transform.position + Vector3.up * mapCameraHeight;
        }

        void ShowResource(Player who)
        {
            if (who == null)
            {
                ammoLabel.text = "2 AMMO/DASH  -";
                ammoFill.fillAmount = 0f;
                return;
            }

            bool showDash = who.Stats.showDashOnHud;
            string resource = showDash ? "DASH"
                : who.TryGetComponent<WeaponModes>(out var wm) ? $"AMMO {wm.Current.displayName}" : "AMMO";
            int current = showDash ? who.DashStacks : who.Ammo.Current;
            int capacity = showDash ? who.DashCapacity : who.Ammo.Capacity;
            ammoLabel.text = $"2 {resource}  {Name(who)}  {current}/{capacity}";
            ammoFill.fillAmount = capacity > 0 ? (float)current / capacity : 0f;
        }

        // 3번 파츠: 대상의 Q 킬 게이지와 E 상태
        void ShowSkill(Player who)
        {
            if (who == null) { dashLabel.text = "3 SKILL  -"; return; }

            var u = who.Ultimate;
            string q = u.IsActive ? $"<color=#D5EF86>Q {u.Data.displayName} {u.ActiveLeft:F1}s</color>"
                : u.IsReady ? "<color=#D5EF86>Q READY</color>"
                : $"Q {u.Kills}/{u.KillsRequired}";

            string e = "";
            if (who.TryGetComponent<Parry>(out var parry))
                e = parry.IsActive ? $"<color=#D5EF86>E PARRY {parry.ActiveLeft:F1}s</color>"
                    : parry.CooldownLeft > 0f ? $"<color=#9AA5B1>E PARRY {parry.CooldownLeft:F1}s</color>"
                    : "<color=#D5EF86>E PARRY READY</color>";
            else if (who.TryGetComponent<WeaponModes>(out var wm))
                e = wm.IsSwitching ? $"<color=#D5EF86>E → {wm.Next.displayName} {wm.SwitchLeft:F1}s</color>"
                    : wm.CooldownLeft > 0f ? $"<color=#9AA5B1>E {wm.Current.displayName} {wm.CooldownLeft:F1}s</color>"
                    : $"<color=#D5EF86>E {wm.Current.displayName} → {wm.Next.displayName}</color>";

            dashLabel.text = $"3 SKILL  {Name(who)}  {q}  {e}";
        }

        void ShowSelf()
        {
            hoverFill.fillAmount = me.Motor.HoverLeft01;

            waveLabel.text =
                waves.WaveNumber == 0 ? "GET READY" :
                waves.AllCleared ? "ALL WAVES CLEAR" :
                waves.BetweenWaves ? $"WAVE {waves.WaveNumber} CLEAR   NEXT IN {waves.SecondsToNextWave:F0}s{(terminal.IsAvailable ? "   <color=#F08045>TERMINAL OPEN</color>" : "")}" :
                $"WAVE {waves.WaveNumber}/{waves.TotalWaves}   ENEMIES {waves.Alive}";

            partsLabel.text = $"{Key(HudPart.Vitals, "1")}  {Key(HudPart.Resource, "2")}  {Key(HudPart.Skill, "3")}  SWAP    {ReloadStatus()}";

            string status = "";
            if (me.Buffs.SpeedMultiplier > 1f) status += "<color=#8FD3FF>RAIL BOOST</color>  ";
            if (me.Buffs.RevealsWeakPoints) status += "<color=#FF6B6B>WEAK POINTS</color>  ";
            if (me.Ultimate.IsActive) status += $"<color=#D5EF86>{me.Ultimate.Data.displayName}</color>  ";
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
            HudPart.Vitals => "VITALS",
            HudPart.Resource => "AMMO/DASH",
            _ => "SKILL",
        };

        // 반환 전 1번 파츠: 파트너를 조준해 회복시켜 줄 수 있으니 쿨타임을 보여 준다
        string Cooldown(HudPart part)
        {
            if (state.IsReturned(part) || partner == null) return "";
            float left = me.GetComponent<SupportGiver>().CooldownLeft;
            return left > 0f ? $"  <color=#9AA5B1>HEAL {left:F1}s</color>" : "  <color=#7CFC9A>HEAL READY</color>";
        }

        // [R] 원격 재장전: 누구를 채우는지와 쿨타임
        string ReloadStatus()
        {
            var r = me.GetComponent<ResourceReload>();
            string who = r.SelfManaged ? "SELF" : "PARTNER";
            float left = r.CooldownLeft;
            return left > 0f ? $"<color=#9AA5B1>[R] RELOAD {who} {left:F1}s</color>" : $"<color=#D5EF86>[R] RELOAD {who}</color>";
        }

        string Key(HudPart part, string key) => state.IsReturned(part) ? $"<color=#7CFC9A>[{key}]</color>" : $"[{key}]";

        static string Name(Player who) =>
            who ? $"<color=#{ColorUtility.ToHtmlStringRGB(who.Stats.color)}>{who.Stats.displayName}</color>" : "-";
    }
}
