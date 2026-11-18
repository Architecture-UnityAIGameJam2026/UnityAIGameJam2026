using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark
{
    /// <summary>
    /// S2 인게임 HUD. 파츠(생체 / 지도 / 소모품)마다 반환 전이면 상대, 반환 후면 내 정보를 보여 준다.
    /// 대쉬 · 호버 · 웨이브는 항상 내 정보다.
    /// </summary>
    public class CrossHudView : MonoBehaviour
    {
        [SerializeField] CrossHudState state;
        [SerializeField] WaveManager waves;
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

        PlayerMotor me, partner;

        void Update()
        {
            FindPlayers();
            root.SetActive(me != null);
            if (me == null) return;

            ShowBio(Subject(HudPart.Bio));
            ShowMap(Subject(HudPart.Map));
            ShowConsumable(Subject(HudPart.Consumable));
            ShowSelf();
        }

        /// <summary>그 파츠로 누구의 정보를 보는지. 상대가 아직 없으면 비워 둔다.</summary>
        PlayerMotor Subject(HudPart part) => state.IsReturned(part) ? me : partner;

        void ShowBio(PlayerMotor who)
        {
            bioLabel.text = $"1 BIO  {Name(who)}";
            bioFill.fillAmount = who ? who.GetComponent<Health>().Ratio : 0f;
        }

        void ShowMap(PlayerMotor who)
        {
            mapLabel.text = $"2 MAP  {Name(who)}";
            mapCamera.enabled = who != null;
            if (who) mapCamera.transform.position = who.transform.position + Vector3.up * mapCameraHeight;
        }

        void ShowConsumable(PlayerMotor who)
        {
            var pool = who ? who.GetComponent<AmmoPool>() : null;
            ammoLabel.text = pool ? $"3 AMMO  {Name(who)}  {pool.Current}/{pool.Capacity}" : $"3 AMMO  {Name(who)}";
            ammoFill.fillAmount = pool ? pool.Ratio : 0f;
        }

        void ShowSelf()
        {
            dashLabel.text = $"DASH {me.DashStacks}/{me.Stats.dashMaxStacks}";
            hoverFill.fillAmount = me.HoverLeft01;

            waveLabel.text = waves.WaveNumber == 0
                ? "GET READY"
                : $"WAVE {waves.WaveNumber}/{waves.TotalWaves}   ENEMIES {waves.Alive}";

            partsLabel.text = $"{Key(HudPart.Bio, "1")}  {Key(HudPart.Map, "2")}  {Key(HudPart.Consumable, "3")}  SWAP";
        }

        string Key(HudPart part, string key) => state.IsReturned(part) ? $"<color=#7CFC9A>[{key}]</color>" : $"[{key}]";

        static string Name(PlayerMotor who) =>
            who ? $"<color=#{ColorUtility.ToHtmlStringRGB(who.Stats.color)}>{who.Stats.displayName}</color>" : "-";

        void FindPlayers()
        {
            if (me != null && partner != null) return;
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsConnectedClient) return;

            foreach (var motor in FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None))
            {
                if (motor.GetComponent<NetworkObject>().IsOwner) me = motor;
                else partner = motor;
            }
        }
    }
}
