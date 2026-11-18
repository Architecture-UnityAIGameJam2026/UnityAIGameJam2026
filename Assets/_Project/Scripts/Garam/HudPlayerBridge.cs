using OnYourMark.UI;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// garam SenseLink HUD에 실제 플레이어 체력·자원을 넣는다.
    /// 내 플레이어 → localHealth/localAmmo, 상대 플레이어 → 파트너 텔레메트리.
    /// HUD는 탄약을 0~8칸으로 표시하므로 AmmoPool 비율을 8칸으로 환산한다.
    /// </summary>
    public class HudPlayerBridge : MonoBehaviour
    {
        [SerializeField] OnYourMarkHud hud;
        [Tooltip("끄면 HUD 데모 값(F1~F4 시연)이 그대로 유지된다. 플레이 중 F6으로 전환.")]
        [SerializeField] bool driveFromPlayers = true;

        const int AmmoSlots = 8;

        void Update()
        {
            if (!hud) return;
            if (UnityEngine.InputSystem.Keyboard.current?.f6Key.wasPressedThisFrame == true)
                driveFromPlayers = !driveFromPlayers;
            if (!driveFromPlayers) return;

            var nm = NetworkManager.Singleton;
            if (!nm || !nm.IsListening) return;

            NetworkObject local = nm.LocalClient?.PlayerObject;
            NetworkObject partner = null;
            foreach (var obj in nm.SpawnManager.SpawnedObjectsList)
                if (obj.IsPlayerObject && obj != local) { partner = obj; break; }

            if (local)
            {
                var h = local.GetComponent<Health>();
                var a = local.GetComponent<AmmoPool>();
                if (h) hud.localHealth = Mathf.Clamp01(h.Ratio);
                if (a) hud.localAmmo = Mathf.RoundToInt(a.Ratio * AmmoSlots);
            }
            if (partner)
            {
                var h = partner.GetComponent<Health>();
                var a = partner.GetComponent<AmmoPool>();
                hud.SetPartnerTelemetry(h ? Mathf.Clamp01(h.Ratio) : 1f, a ? Mathf.RoundToInt(a.Ratio * AmmoSlots) : AmmoSlots);
            }
        }
    }
}
