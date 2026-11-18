using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace OnYourMark
{
    /// <summary>
    /// 적 공통 동작. AI는 서버에서만 돌고(클라이언트는 NetworkTransform으로 위치만 받음),
    /// 체력이 0이 되면 사라진다.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(NavMeshAgent))]
    public abstract class EnemyBase : NetworkBehaviour
    {
        protected NavMeshAgent Agent { get; private set; }
        protected Health Health { get; private set; }

        protected abstract EnemyData Data { get; }

        double stunnedUntil;   // 서버 시간
        /// <summary>경직 중이면 AI가 멈춘다 (패링에 막혔을 때).</summary>
        protected bool IsStunned => IsSpawned && NetworkManager.ServerTime.Time < stunnedUntil;

        /// <summary>서버 전용. seconds 동안 이동 · 공격을 멈춘다.</summary>
        public void Stun(float seconds)
        {
            if (!IsServer) return;
            stunnedUntil = NetworkManager.ServerTime.Time + seconds;
            if (Agent.isOnNavMesh) Agent.ResetPath();
        }

        public override void OnNetworkSpawn()
        {
            Agent = GetComponent<NavMeshAgent>();
            Health = GetComponent<Health>();
            if (!IsServer) { Agent.enabled = false; return; }

            Agent.speed = Data.moveSpeed;
            Health.Died += _ => NetworkObject.Despawn();
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
