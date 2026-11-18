using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// R 재장전과 2번 파츠(탄창 · 대쉬 수) 규칙.
    /// - 2번 파츠 반환 전: R은 파트너의 자원(P2 탄창 / P1 대쉬)을 거리 · 조준 조건 없이 100% 채운다.
    ///   내 자원은 스스로 채울 수 없고 자동으로도 차지 않는다 (파트너가 채워 줘야 한다).
    /// - 2번 파츠 반환 후: R은 내 자원을 채우고, 자원이 자동으로 다시 찬다.
    /// 쿨타임은 ReloadConfig에서 읽는다.
    /// </summary>
    public class ResourceReload : NetworkBehaviour
    {
        [SerializeField] ReloadConfig config;
        [SerializeField] InputActionReference reload;

        readonly NetworkVariable<double> readyAt = new();   // 쿨타임 종료 시각 (서버 시간)

        Player me;
        CrossHudState hud;
        float regenTimer;

        public float CooldownLeft => IsSpawned ? Mathf.Max(0f, (float)(readyAt.Value - NetworkManager.ServerTime.Time)) : 0f;
        /// <summary>2번 파츠를 돌려받아 내 자원을 내가 관리하는지</summary>
        public bool SelfManaged => hud != null && hud.IsReturned(HudPart.Consumable);
        /// <summary>지금 R을 누르면 채워지는 대상</summary>
        public Player Target => SelfManaged ? me : me.Partner;

        void Awake() => me = GetComponent<Player>();

        public override void OnNetworkSpawn() => hud = FindFirstObjectByType<CrossHudState>();

        void Update()
        {
            if (!IsSpawned || hud == null) return;

            if (IsOwner)
            {
                // 대쉬는 내 클라이언트가 계산하므로, 자동 충전 허용 여부도 여기서 알려 준다
                me.Motor.AutoRechargeDash = SelfManaged;
                if (CursorLock.IsLocked && reload.action.WasPressedThisFrame()) ReloadRpc();
            }

            if (IsServer) RegenerateAmmo();
        }

        [Rpc(SendTo.Server)]
        void ReloadRpc()
        {
            var target = Target;
            if (CooldownLeft > 0f || me.Health.IsDead || target == null || target.Health.IsDead) return;
            Refill(target);
            readyAt.Value = NetworkManager.ServerTime.Time + config.cooldown;
        }

        // 서버에서 호출. 탄창은 서버가, 대쉬는 그 플레이어의 클라이언트가 관리한다
        static void Refill(Player target)
        {
            if (target.Stats.showDashOnHud) target.GetComponent<ResourceReload>().FillDashRpc();
            else target.Ammo.Fill();
        }

        [Rpc(SendTo.Owner)]
        void FillDashRpc() => me.Motor.ResetDash();

        // 2번 파츠 반환 후 탄창 자동 재생 (서버)
        void RegenerateAmmo()
        {
            if (!SelfManaged || me.Stats.showDashOnHud || me.Health.IsDead || me.Ammo.IsFull
                || Time.time - me.Ammo.LastSpendTime < config.ammoRegenDelay)
            {
                regenTimer = 0f;
                return;
            }
            regenTimer += Time.deltaTime;
            if (regenTimer < config.ammoRegenInterval) return;
            regenTimer = 0f;
            me.Ammo.Add(1);
        }
    }
}
