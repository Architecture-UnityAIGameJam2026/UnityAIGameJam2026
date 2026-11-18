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
        Player[] pilots;
        [SerializeField, Min(0)] float soloWaitSeconds = 5f;
        float soloFillAt = float.PositiveInfinity;
        public int SoloSecondsLeft => Mathf.Max(0, Mathf.CeilToInt(soloFillAt-Time.unscaledTime));

        void Update()
        {
            if (IsHost && !spawnDone && Time.unscaledTime >= soloFillAt &&
                NetworkManager.ConnectedClients.Count == 1) FillWithBot();
        }

        public RoleDefinition[] Roles => roles;
        public ulong OwnerOf(int role) => role < owners.Count ? owners[role] : Nobody;
        public bool HasChosen(ulong clientId) => owners.Contains(clientId);
        /// <summary>두 역할이 모두 정해짐 (진행 중이거나 결과 화면). 재시작하면 다시 false.</summary>
        public bool AllChosen => owners.Count > 0 && !owners.Contains(Nobody);

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            owners.Clear();
            soloFillAt = float.PositiveInfinity;
            pilots = new Player[roles.Length];
            spawnDone = false;
            foreach (var _ in roles) owners.Add(Nobody);
            NetworkManager.OnClientDisconnectCallback += ReleaseRole;
            NetworkManager.OnClientConnectedCallback += TakeBotSeat;
            roundReset.Raised += ClearRoles;
        }

        void ClearRoles()
        {
            for (int i = 0; i < owners.Count; i++) owners[i] = Nobody;
            spawnDone = false;
            soloFillAt = float.PositiveInfinity;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;
            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= ReleaseRole;
                NetworkManager.OnClientConnectedCallback -= TakeBotSeat;
            }
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
            if (IsHost && sender == NetworkManager.LocalClientId) soloFillAt = Time.unscaledTime+soloWaitSeconds;
            if (!owners.Contains(Nobody)) SpawnPlayers();
        }

        public const ulong BotSlot = ulong.MaxValue - 1;

        public void FillWithBot()
        {
            if (!IsHost || spawnDone || NetworkManager.ConnectedClients.Count != 1 || !HasChosen(NetworkManager.LocalClientId)) return;
            for (int i = 0; i < owners.Count; i++)
                if (owners[i] == Nobody) owners[i] = BotSlot;
            SpawnPlayers();
        }

        void SpawnPlayers()
        {
            spawnDone = true;
            for (int i = 0; i < roles.Length; i++)
            {
                var point = SpawnPoint.For(roles[i]);
                var player = Instantiate(roles[i].playerPrefab, point.position, point.rotation);
                pilots[i] = player.GetComponent<Player>();
                if (owners[i] == BotSlot)
                {
                    player.Spawn(destroyWithScene: true);
                    player.gameObject.AddComponent<RadioCompanion>();
                }
                else { player.DontDestroyWithOwner = true; player.SpawnAsPlayerObject(owners[i], true); }
            }
            playersSpawned.Raise();
        }

        // A joining player inherits the AI's role, location, health and ammunition.
        static void InheritPilot(Player next, Player previous)
        {
            next.Health.Inherit(previous.Health);
            if (next.TryGetComponent<WeaponModes>(out var modes)) modes.Inherit(previous.GetComponent<WeaponModes>());
            next.Ammo.SetMagazine(previous.Ammo.Capacity, previous.Ammo.Current);
        }

        void TakeBotSeat(ulong clientId)
        {
            if (!spawnDone || HasChosen(clientId)) return;
            var bot = System.Linq.Enumerable.FirstOrDefault(Player.All, p => p.IsBot);
            if (!bot) return;
            for (int i = 0; i < owners.Count; i++)
            {
                if (owners[i] != BotSlot) continue;
                var replacement = Instantiate(roles[i].playerPrefab, bot.transform.position, bot.transform.rotation);
                owners[i] = clientId;
                replacement.DontDestroyWithOwner = true;
                replacement.SpawnAsPlayerObject(clientId, true);
                var human = replacement.GetComponent<Player>();
                pilots[i] = human;
                InheritPilot(human, bot);
                bot.NetworkObject.Despawn();
                return;
            }
        }

        // 고르는 도중 나간 플레이어의 자리를 비운다
        void ReleaseRole(ulong clientId)
        {
            for (int i = 0; i < owners.Count; i++)
            {
                if (owners[i] != clientId) continue;
                if (!spawnDone) { owners[i] = Nobody; continue; }
                if (!IsHost || NetworkManager.ShutdownInProgress || clientId == NetworkManager.LocalClientId || !Player.Local) return;
                var departing = pilots[i];
                var at = departing ? departing.transform : SpawnPoint.For(roles[i]).transform;
                var replacement = Instantiate(roles[i].playerPrefab, at.position, at.rotation);
                owners[i] = BotSlot;
                replacement.Spawn(destroyWithScene: true);
                var ai = replacement.GetComponent<Player>();
                pilots[i] = ai;
                if (departing && departing.IsSpawned)
                {
                    InheritPilot(ai, departing);
                    departing.NetworkObject.Despawn();
                }
                replacement.gameObject.AddComponent<RadioCompanion>();
            }
        }
    }
}
