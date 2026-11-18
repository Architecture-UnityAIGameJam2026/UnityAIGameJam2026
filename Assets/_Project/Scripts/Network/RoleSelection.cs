using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 포지션 선점(서버 권한). 역할마다 차지한 클라이언트 ID를 NetworkList로 동기화하고,
    /// 모든 역할이 채워지면 각자의 역할 프리팹을 해당 스폰 포인트에 생성한다.
    /// </summary>
    public class RoleSelection : NetworkBehaviour
    {
        public const ulong Nobody = ulong.MaxValue;

        [SerializeField] RoleDefinition[] roles;
        [Tooltip("두 플레이어가 모두 스폰되면 Raise (웨이브 시작 등)")]
        [SerializeField] GameEvent playersSpawned;
        [Tooltip("재시작 시 Raise: 포지션을 다시 고른다")]
        [SerializeField] GameEvent roundReset;

        readonly NetworkList<ulong> owners = new();
        bool spawnDone;

        public RoleDefinition[] Roles => roles;
        public ulong OwnerOf(int role) => role < owners.Count ? owners[role] : Nobody;
        public bool HasChosen(ulong clientId) => owners.Contains(clientId);
        /// <summary>두 역할이 모두 정해짐 (진행 중이거나 결과 화면). 재시작하면 다시 false.</summary>
        public bool AllChosen => owners.Count > 0 && !owners.Contains(Nobody);

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            owners.Clear();
            foreach (var _ in roles) owners.Add(Nobody);
            NetworkManager.OnClientDisconnectCallback += ReleaseRole;
            roundReset.Raised += ClearRoles;
        }

        void ClearRoles()
        {
            for (int i = 0; i < owners.Count; i++) owners[i] = Nobody;
            spawnDone = false;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;
            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= ReleaseRole;
            roundReset.Raised -= ClearRoles;
        }

        public void Choose(int role) => ChooseRpc(role);

        [Rpc(SendTo.Server)]
        void ChooseRpc(int role, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            // 먼저 도착한 요청만 인정: 이미 찼거나, 이미 고른 사람이면 무시
            if (spawnDone || role < 0 || role >= roles.Length) return;
            if (OwnerOf(role) != Nobody || HasChosen(sender)) return;

            owners[role] = sender;
            if (!owners.Contains(Nobody)) SpawnPlayers();
        }

        void SpawnPlayers()
        {
            spawnDone = true;
            for (int i = 0; i < roles.Length; i++)
            {
                var point = SpawnPoint.For(roles[i]);
                var player = Instantiate(roles[i].playerPrefab, point.position, point.rotation);
                player.SpawnAsPlayerObject(owners[i]);
            }
            playersSpawned.Raise();
        }

        // 고르는 도중 나간 플레이어의 자리를 비운다
        void ReleaseRole(ulong clientId)
        {
            if (spawnDone) return;
            for (int i = 0; i < owners.Count; i++)
                if (owners[i] == clientId) owners[i] = Nobody;
        }
    }
}
