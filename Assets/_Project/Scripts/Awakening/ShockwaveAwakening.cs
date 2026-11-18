using UnityEngine;
using UnityEngine.AI;

namespace OnYourMark
{
    /// <summary>P2 생체 각성: 처치 시 주변 적을 밀어내는 충격파 + 자가 회복.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Shockwave")]
    public class ShockwaveAwakening : Awakening
    {
        public float radius = 4f;
        public float pushDistance = 3f;
        public float selfHeal = 15f;

        public override void OnKill(AwakeningSet owner)
        {
            Vector3 center = owner.transform.position;
            foreach (var enemy in EnemiesAround(center, radius))
            {
                Vector3 away = enemy.transform.position - center; away.y = 0f;
                if (enemy.TryGetComponent<NavMeshAgent>(out var agent) && agent.isOnNavMesh)
                    agent.Move(away.normalized * pushDistance);
            }
            owner.Me.Health.Heal(selfHeal);
            owner.PlayEffect(this, center + Vector3.up * 0.1f, Quaternion.identity, radius * 2f);
        }
    }
}
