using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace OnYourMark
{
    /// <summary>
    /// 적 공통 동작. AI는 서버에서만 돌고(클라이언트는 NetworkTransform으로 위치만 받음),
    /// 체력이 0이 되면 사라진다. NetworkObjectPool로 재사용되므로 스폰마다 상태를 초기화하고
    /// 디스폰 때 구독을 푼다 (파생 클래스도 OnNetworkSpawn에서 자기 상태를 되돌린다).
    /// </summary>
    [RequireComponent(typeof(Health), typeof(NavMeshAgent))]
    public abstract class EnemyBase : NetworkBehaviour
    {
        protected NavMeshAgent Agent { get; private set; }
        protected Health Health { get; private set; }

        protected abstract EnemyData Data { get; }

        public override void OnNetworkSpawn()
        {
            Agent = GetComponent<NavMeshAgent>();
            Health = GetComponent<Health>();
            if (!IsServer) { Agent.enabled = false; return; }

            Agent.enabled = true;
            if (Agent.isOnNavMesh) { Agent.Warp(transform.position); Agent.isStopped = false; Agent.ResetPath(); }
            Agent.speed = Data.moveSpeed;
            Health.Died -= OnDied;
            Health.Died += OnDied;
        }

        public override void OnNetworkDespawn()
        {
            if (Health) Health.Died -= OnDied;
            StopAllCoroutines();
        }

        void OnDied(Health _)
        {
            if (IsSpawned) NetworkObject.Despawn();
        }

        /// <summary>살아 있는 플레이어 중 가장 가까운 대상.</summary>
        protected Health NearestPlayer()
        {
            Health best = null;
            float bestDistance = float.MaxValue;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent<Health>(out var h) || h.IsDead) continue;
                float d = (h.transform.position - transform.position).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = h; }
            }
            return best;
        }

        protected static Vector3 Chest(Health target) => target.transform.position + Vector3.up * 1.1f;
    }
}
