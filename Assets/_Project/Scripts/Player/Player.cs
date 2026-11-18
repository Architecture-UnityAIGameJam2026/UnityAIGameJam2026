using System.Collections.Generic;
using Unity.Netcode;

namespace OnYourMark
{
    /// <summary>플레이어 한 명의 컴포넌트 묶음과, 접속한 플레이어 목록 (서버 · 클라이언트 모두에서 동작).</summary>
    public class Player : NetworkBehaviour
    {
        static readonly List<Player> all = new();
        // 이동은 소유자만 계산하므로, 남은 대쉬 횟수도 소유자가 공유한다.
        readonly NetworkVariable<int> dashStacks = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<int> dashCapacity = new(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public static IReadOnlyList<Player> All => all;
        public static Player Local => all.Find(p => p.IsOwner);
        public Player Partner => all.Find(p => p != this);

        public PlayerMotor Motor { get; private set; }
        public Health Health { get; private set; }
        public AmmoPool Ammo { get; private set; }
        public PlayerBuffs Buffs { get; private set; }
        public Ultimate Ultimate { get; private set; }
        public PlayerStats Stats => Motor.Stats;
        public int DashStacks => IsOwner ? Motor.DashStacks : dashStacks.Value;
        /// <summary>지금의 최대 대쉬 횟수 (스킬로 늘어날 수 있다)</summary>
        public int DashCapacity => IsOwner ? Motor.DashCapacity : UnityEngine.Mathf.Max(dashCapacity.Value, Stats.dashMaxStacks);

        void Awake()
        {
            Motor = GetComponent<PlayerMotor>();
            Health = GetComponent<Health>();
            Ammo = GetComponent<AmmoPool>();
            Buffs = GetComponent<PlayerBuffs>();
            Ultimate = GetComponent<Ultimate>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) Health.DamageTakenMultiplier = PartnerSwitchProtection;
            if (IsOwner) { dashStacks.Value = Motor.DashStacks; dashCapacity.Value = Motor.DashCapacity; }
            all.Add(this);
            UnityEngine.Debug.Log($"[OnYourMark] {Stats.displayName} spawned for client {OwnerClientId} at {transform.position} (mine: {IsOwner})");
        }

        void LateUpdate()
        {
            if (!IsSpawned || !IsOwner) return;
            if (dashStacks.Value != Motor.DashStacks) dashStacks.Value = Motor.DashStacks;
            if (dashCapacity.Value != Motor.DashCapacity) dashCapacity.Value = Motor.DashCapacity;
        }

        public override void OnNetworkDespawn() => all.Remove(this);

        CrossHudState hud;

        // 시너지: 3번 파츠 반환 전, 파트너(P2)가 무기를 변형하는 동안 나는 피해를 덜 받는다
        float PartnerSwitchProtection()
        {
            if (hud == null) hud = FindFirstObjectByType<CrossHudState>();
            var partner = Partner;
            if (hud == null || hud.IsReturned(HudPart.Skill) || partner == null) return 1f;
            return partner.TryGetComponent<WeaponModes>(out var wm) && wm.IsSwitching ? wm.Data.partnerDamageTakenWhileSwitching : 1f;
        }
    }
}
