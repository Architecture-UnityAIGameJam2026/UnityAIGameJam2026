using OnYourMark.UI;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// garam SenseLink HUD에 실제 플레이어 체력·자원을 넣는다.
    /// 내 플레이어 → localHealth/localAmmo, 상대 플레이어 → 파트너 텔레메트리.
    /// HUD는 탄약을 0~8칸으로 표시하므로 AmmoPool 비율을 8칸으로 환산한다.
    /// 파트너가 처음 잡히는 순간(서로의 HUD를 들고 시작) HudSwapGlitch 크로스 링크를 재생한다.
    /// 플레이어 컴포넌트는 바뀔 때만 다시 찾고(매 프레임 GetComponent 없음), 파트너 탐색은 0.5초 간격이다.
    /// </summary>
    public class HudPlayerBridge : MonoBehaviour
    {
        [SerializeField] OnYourMarkHud hud;
        [Tooltip("비워 두면 HUD 오브젝트에서 찾는다")]
        [SerializeField] HudSwapGlitch swapGlitch;
        [Tooltip("끄면 HUD 데모 값(F1~F4 시연)이 그대로 유지된다. 플레이 중 F6으로 전환.")]
        [SerializeField] bool driveFromPlayers = true;

        const int AmmoSlots = 8;
        const float PartnerScanInterval = 0.5f;

        NetworkObject local, partner;
        Health localHealth, partnerHealth;
        AmmoPool localAmmo, partnerAmmo;
        float nextPartnerScan;
        bool linked;

        void Awake()
        {
            if (!swapGlitch && hud) swapGlitch = hud.GetComponent<HudSwapGlitch>();
        }

        void Update()
        {
            if (!hud) return;
            if (UnityEngine.InputSystem.Keyboard.current?.f6Key.wasPressedThisFrame == true)
                driveFromPlayers = !driveFromPlayers;
            if (!driveFromPlayers) return;

            var nm = NetworkManager.Singleton;
            if (!nm || !nm.IsListening) { ForgetPartner(); SetLocal(null); return; }

            var me = nm.LocalClient?.PlayerObject;
            if (me != local) SetLocal(me);

            if (partner && !partner.IsSpawned) ForgetPartner();
            if (!partner && Time.unscaledTime >= nextPartnerScan)
            {
                nextPartnerScan = Time.unscaledTime + PartnerScanInterval;
                FindPartner(nm);
            }

            if (local)
            {
                if (localHealth) hud.localHealth = Mathf.Clamp01(localHealth.Ratio);
                if (localAmmo) hud.localAmmo = Mathf.RoundToInt(localAmmo.Ratio * AmmoSlots);
            }
            if (partner)
            {
                hud.SetPartnerTelemetry(partnerHealth ? Mathf.Clamp01(partnerHealth.Ratio) : 1f,
                                        partnerAmmo ? Mathf.RoundToInt(partnerAmmo.Ratio * AmmoSlots) : AmmoSlots);
            }
        }

        void SetLocal(NetworkObject me)
        {
            local = me;
            localHealth = me ? me.GetComponent<Health>() : null;
            localAmmo = me ? me.GetComponent<AmmoPool>() : null;
        }

        void FindPartner(NetworkManager nm)
        {
            foreach (var obj in nm.SpawnManager.SpawnedObjectsList)
            {
                if (!obj.IsPlayerObject || obj == local) continue;
                partner = obj;
                partnerHealth = obj.GetComponent<Health>();
                partnerAmmo = obj.GetComponent<AmmoPool>();
                if (!linked)
                {
                    linked = true;
                    if (swapGlitch) swapGlitch.PlayCrossLink(toSelf: false);   // 이제 서로의 감각을 본다
                }
                return;
            }
        }

        void ForgetPartner()
        {
            partner = null; partnerHealth = null; partnerAmmo = null;
            linked = false;
        }
    }
}
