using System.Collections.Generic;
using Unity.Netcode;

namespace OnYourMark
{
    /// <summary>플레이어 한 명의 컴포넌트 묶음과, 접속한 플레이어 목록 (서버 · 클라이언트 모두에서 동작).</summary>
    public class Player : NetworkBehaviour
    {
        static readonly List<Player> all = new();

        public static IReadOnlyList<Player> All => all;
        public static Player Local => all.Find(p => p.IsOwner);
        public Player Partner => all.Find(p => p != this);

        public PlayerMotor Motor { get; private set; }
        public Health Health { get; private set; }
        public AmmoPool Ammo { get; private set; }
        public PlayerBuffs Buffs { get; private set; }
        public SupportRequests Requests { get; private set; }
        public PlayerStats Stats => Motor.Stats;

        void Awake()
        {
            Motor = GetComponent<PlayerMotor>();
            Health = GetComponent<Health>();
            Ammo = GetComponent<AmmoPool>();
            Buffs = GetComponent<PlayerBuffs>();
            Requests = GetComponent<SupportRequests>();
        }

        public override void OnNetworkSpawn() => all.Add(this);
        public override void OnNetworkDespawn() => all.Remove(this);
    }
}
