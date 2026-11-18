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

        // Confirmed server-side outcomes shared by human and companion gameplay.
        public static event System.Action<Player, string, string> ActionResolved;
        public void ReportAction(string action, string detail)
        {
            if (IsServer && IsSpawned) ActionResolved?.Invoke(this, action, detail);
        }

        public static IReadOnlyList<Player> All => all;
        byte requestedSupport;
        public void RequestSupport(HudPart part) { if (IsServer) requestedSupport |= (byte)(1 << (int)part); }
        public void FulfillSupport(HudPart part) { if (IsServer) requestedSupport &= (byte)~(1 << (int)part); }
        public bool NeedsSupport(HudPart part)
        {
            if ((requestedSupport & (1 << (int)part)) != 0) return true;
            return part switch
            {
                HudPart.Vitals => !Health.IsDead && Health.Ratio <= 0.4f,
                HudPart.Resource => Stats.showDashOnHud ? DashStacks == 0 : Ammo.Ratio <= 0.15f,
                _ => false
            };
        }

        public bool IsBot => IsSpawned && !NetworkObject.IsPlayerObject;
        public static Player Local => all.Find(p => p.IsOwner && !p.IsBot);
        public string Role => Stats.CanZoom ? "ranged" : "melee";
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

        public void MoveToCheckpoint(UnityEngine.Vector3 position)
        {
            if (IsServer) WarpOwnerRpc(position);
        }

        [Rpc(SendTo.Owner)]
        void WarpOwnerRpc(UnityEngine.Vector3 position)
        {
            Motor.ResetMotion(position);
            GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(position, transform.rotation, transform.localScale);
        }

        public override void OnNetworkDespawn() => all.Remove(this);
    }
}
