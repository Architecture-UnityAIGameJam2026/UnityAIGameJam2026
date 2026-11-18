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

        // Confirmed server-side outcomes shared by human and companion gameplay.
        public static event System.Action<Player, string, string> ActionResolved;
        public void ReportAction(string action, string detail)
        {
            if (IsServer && IsSpawned) ActionResolved?.Invoke(this, action, detail);
        }

        public static IReadOnlyList<Player> All => all;
        public bool IsBot => IsSpawned && !NetworkObject.IsPlayerObject;
        public static Player Local => all.Find(p => p.IsOwner && !p.IsBot);
        public string Role => Stats.CanZoom ? "ranged" : "melee";
        public Player Partner => all.Find(p => p != this);

        public PlayerMotor Motor { get; private set; }
        public Health Health { get; private set; }
        public AmmoPool Ammo { get; private set; }
        public PlayerBuffs Buffs { get; private set; }
        public SupportRequests Requests { get; private set; }
        public PlayerStats Stats => Motor.Stats;
        public int DashStacks => IsOwner ? Motor.DashStacks : dashStacks.Value;

        void Awake()
        {
            Motor = GetComponent<PlayerMotor>();
            Health = GetComponent<Health>();
            Ammo = GetComponent<AmmoPool>();
            Buffs = GetComponent<PlayerBuffs>();
            Requests = GetComponent<SupportRequests>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner) dashStacks.Value = Motor.DashStacks;
            all.Add(this);
            UnityEngine.Debug.Log($"[OnYourMark] {Stats.displayName} spawned for client {OwnerClientId} at {transform.position} (mine: {IsOwner})");
        }

        void LateUpdate()
        {
            if (IsSpawned && IsOwner && dashStacks.Value != Motor.DashStacks)
                dashStacks.Value = Motor.DashStacks;
        }

        public override void OnNetworkDespawn() => all.Remove(this);
    }
}
