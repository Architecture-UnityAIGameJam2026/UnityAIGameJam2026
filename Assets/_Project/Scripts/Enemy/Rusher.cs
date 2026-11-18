using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>근접 자폭형. 가장 가까운 플레이어를 추적하다 범위 안에 들어오면 멈추고, 카운트다운 후 폭발한다.</summary>
    public class Rusher : EnemyBase
    {
        [SerializeField] RusherData data;
        [SerializeField] GameObject fuseWarning;
        [SerializeField] GameObject blastEffect;

        bool fusing;
        readonly HashSet<Health> blastTargets = new();
        int coverMask;

        protected override EnemyData Data => data;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            coverMask = LayerMask.GetMask("Environment");
            fusing = false;   // 풀에서 재사용: 이전 생의 퓨즈 상태를 지운다
            if (fuseWarning) fuseWarning.SetActive(false);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || fusing || IsStunned) return;

            var target = NearestPlayer();
            if (target == null) { Agent.ResetPath(); return; }

            if (Vector3.Distance(transform.position, target.transform.position) <= data.triggerRange)
                StartCoroutine(Detonate());
            else
                Agent.SetDestination(target.transform.position);
        }

        IEnumerator Detonate()
        {
            fusing = true;
            Agent.isStopped = true;
            ShowFuseRpc();
            yield return new WaitForSeconds(data.fuseTime);

            Vector3 origin = transform.position + Vector3.up * 0.8f;
            blastTargets.Clear();
            foreach (var c in Physics.OverlapSphere(transform.position, data.blastRadius, data.targetMask))
                if (Health.Of(c) is { IsDead: false } h && h != Health && blastTargets.Add(h)
                    && !Physics.Linecast(origin, Chest(h), coverMask, QueryTriggerInteraction.Ignore)
                    && !Parry.Blocks(h, transform.position, true, this))
                    h.Damage(data.blastDamage);

            ShowBlastRpc(transform.position);
            Health.Damage(Health.Max);   // 자폭: 사망 처리(웨이브 카운트, 디스폰)를 그대로 탄다
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ShowFuseRpc() => fuseWarning.SetActive(true);

        [Rpc(SendTo.ClientsAndHost)]
        void ShowBlastRpc(Vector3 position)
        {
            FxPool.Play(blastEffect, position, Quaternion.identity, Vector3.one * data.blastRadius * 2f, 0.3f);
        }
    }
}
