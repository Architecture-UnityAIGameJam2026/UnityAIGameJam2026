using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// R 재장전과 2번 파츠(탄창 · 대쉬 수) 규칙.
    /// - 2번 파츠 반환 전 (역전된 리필 &amp; 오버차지): 나는 내 자원을 R로도, 자연 충전으로도 채울 수 없다.
    ///   파트너가 R을 누르면 즉시 100% 충전 + 오버차지
    ///   (P2: 탄창 150% 초과 장전, 초과 탄환 대미지 +30% / P1: 대쉬 완충 + 1초 무한 스텝).
    ///   그래서 R은 거리 · 조준 조건 없이 파트너를 채운다.
    /// - 2번 파츠 반환 후: R은 내 자원을 채우고(오버차지 없음), 대쉬 자연 충전이 돌아온다.
    ///   포지션별 피드백 각성은 Awakening(처치 시 대쉬 +1 / 대쉬 시 즉시 장전)이 맡는다.
    /// </summary>
    public class ResourceReload : NetworkBehaviour
    {
        [SerializeField] ReloadConfig config;
        [SerializeField] InputActionReference reload;

        readonly NetworkVariable<double> readyAt = new();   // 쿨타임 종료 시각 (서버 시간)

        Player me;
        CrossHudState hud;

        public float CooldownLeft => IsSpawned ? Mathf.Max(0f, (float)(readyAt.Value - NetworkManager.ServerTime.Time)) : 0f;
        /// <summary>2번 파츠를 돌려받아 내 자원을 내가 관리하는지</summary>
        public bool SelfManaged => hud != null && hud.IsReturned(HudPart.Resource);
        /// <summary>지금 R을 누르면 채워지는 대상</summary>
        public Player Target => SelfManaged ? me : me.Partner;

        void Awake() => me = GetComponent<Player>();

        public override void OnNetworkSpawn() => hud = FindFirstObjectByType<CrossHudState>();

        void Update()
        {
            if (!IsSpawned || hud == null || !IsOwner) return;
            // 대쉬는 내 클라이언트가 계산하므로, 자연 충전 허용 여부도 여기서 알려 준다
            me.Motor.AutoRechargeDash = SelfManaged;
            if (NetworkObject.IsPlayerObject && CursorLock.IsLocked && reload.action.WasPressedThisFrame()) ReloadRpc();
        }

        [Rpc(SendTo.Server)]
        void ReloadRpc() => TryReload();

        public bool TryReload()
        {
            var target = Target;
            if (!IsServer || CooldownLeft > 0f || me.Health.IsDead || target == null || target.Health.IsDead) return false;

            bool overcharge = !SelfManaged;   // 파트너를 채워 줄 때만 오버차지
            if (target.Stats.showDashOnHud)
                target.GetComponent<ResourceReload>().FillDashRpc(overcharge ? config.infiniteDashSeconds : 0f);
            else if (overcharge)
                target.Ammo.FillTo(Mathf.CeilToInt(target.Ammo.Capacity * config.magazineOverchargeRatio));
            else
                target.Ammo.Fill();

            readyAt.Value = NetworkManager.ServerTime.Time + config.cooldown;
            SoundRelay.All(SoundId.RemoteReload, target.transform.position + Vector3.up);
            if (overcharge && target.Stats.showDashOnHud && config.infiniteDashSeconds > 0f)
                SoundRelay.All(SoundId.InfiniteStep, target.transform.position + Vector3.up);
            target.FulfillSupport(HudPart.Resource);
            me.ReportAction(SelfManaged ? "reload" : "grant_resource", "completed");
            return true;
        }

        [Rpc(SendTo.Owner)]
        void FillDashRpc(float infiniteSeconds) => me.Motor.ResetDash(infiniteSeconds);
    }
}
