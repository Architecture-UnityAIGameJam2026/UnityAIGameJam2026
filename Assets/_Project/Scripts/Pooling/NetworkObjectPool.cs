using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 네트워크 프리팹(투사체 · 적) 풀. NGO 프리팹 핸들러(INetworkPrefabInstanceHandler)로 등록해서
    /// 서버와 클라이언트 모두 Spawn/Despawn 때 Instantiate/Destroy 대신 비활성 인스턴스를 재사용한다.
    ///
    /// 사용:
    /// - 모든 피어에서 첫 스폰 전에 Register(prefab). (해당 시스템의 Start/OnNetworkSpawn에서 부르면 된다. 여러 번 불러도 안전)
    /// - 서버: var obj = NetworkObjectPool.Get(prefab, pos, rot); ... obj.Spawn();
    /// - 반환: 기존처럼 NetworkObject.Despawn() (destroy=true 기본값) → 서버·클라이언트 모두 풀로 돌아온다.
    ///
    /// 주의: 재사용된 인스턴스는 컴포넌트 필드 값을 그대로 들고 온다. 상태는 OnNetworkSpawn에서 초기화하고,
    /// 이벤트 구독은 OnNetworkDespawn에서 해제한다 (EnemyBase / Rusher / HoverLaser / Projectile 참고).
    /// 등록 전에 받은 스폰(늦게 들어온 클라이언트 등)은 NGO 기본 경로(Instantiate)로 처리되므로 깨지지 않는다.
    /// </summary>
    public sealed class NetworkObjectPool : INetworkPrefabInstanceHandler
    {
        static readonly Dictionary<GameObject, NetworkObjectPool> Pools = new();
        static NetworkManager s_Owner;

        readonly GameObject prefab;
        readonly Stack<NetworkObject> free = new();

        public int CountInactive => free.Count;
        public int CountCreated { get; private set; }

        NetworkObjectPool(GameObject prefab) => this.prefab = prefab;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Pools.Clear(); s_Owner = null; }   // 도메인 리로드를 끈 플레이 모드 대비

        /// <summary>프리팹을 풀에 등록한다. prewarm만큼 미리 만들어 둔다 (NavMeshAgent가 있는 프리팹은 0 권장).</summary>
        public static void Register(NetworkObject prefab, int prewarm = 0)
        {
            var nm = NetworkManager.Singleton;
            if (!prefab || !nm) return;
            if (s_Owner != nm) { Pools.Clear(); s_Owner = nm; }   // NetworkManager가 새로 생기면 핸들러도 새로 건다
            var key = prefab.gameObject;
            if (Pools.ContainsKey(key)) return;

            var pool = new NetworkObjectPool(key);
            if (!nm.PrefabHandler.AddHandler(key, pool))
            {
                Debug.LogWarning($"[NetworkObjectPool] could not register a handler for {prefab.name}");
                return;
            }
            Pools[key] = pool;
            for (int i = 0; i < prewarm; i++) pool.Return(pool.Create(Vector3.zero, Quaternion.identity));
        }

        /// <summary>서버 전용: 풀에서 꺼내(없으면 생성) 위치를 잡은 활성 인스턴스. 호출한 쪽에서 Spawn()한다.</summary>
        public static NetworkObject Get(NetworkObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab && s_Owner && s_Owner == NetworkManager.Singleton && Pools.TryGetValue(prefab.gameObject, out var pool))
                return pool.Take(position, rotation);
            return Object.Instantiate(prefab, position, rotation);   // 등록 안 됨: 예전 동작 그대로
        }

        public static bool TryGetStats(NetworkObject prefab, out int created, out int inactive)
        {
            created = inactive = 0;
            if (!prefab || !Pools.TryGetValue(prefab.gameObject, out var pool)) return false;
            created = pool.CountCreated; inactive = pool.CountInactive;
            return true;
        }

        NetworkObject Take(Vector3 position, Quaternion rotation)
        {
            while (free.Count > 0)
            {
                var obj = free.Pop();
                if (!obj) continue;   // 씬과 함께 파괴된 인스턴스는 버린다
                obj.transform.SetPositionAndRotation(position, rotation);
                obj.gameObject.SetActive(true);
                return obj;
            }
            return Create(position, rotation);
        }

        NetworkObject Create(Vector3 position, Quaternion rotation)
        {
            CountCreated++;
            var go = Object.Instantiate(prefab, position, rotation);
            go.name = prefab.name;   // "(Clone)" 꼬리 없이
            return go.GetComponent<NetworkObject>();
        }

        void Return(NetworkObject obj)
        {
            if (!obj) return;
            obj.gameObject.SetActive(false);
            free.Push(obj);
        }

        NetworkObject INetworkPrefabInstanceHandler.Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
            => Take(position, rotation);

        void INetworkPrefabInstanceHandler.Destroy(NetworkObject networkObject) => Return(networkObject);
    }
}
