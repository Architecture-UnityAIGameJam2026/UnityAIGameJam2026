using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 서버에서 직선으로 날아가며 앞쪽을 SphereCast로 검사한다. 적중하거나 사거리를 다 날면 사라진다.
    /// NetworkObjectPool로 재사용되므로 발사 상태는 Launch에서 매번 초기화한다.
    /// </summary>
    public class Projectile : NetworkBehaviour
    {
        ProjectileData data;
        Vector3 direction;
        float traveled;
        Player shooter;
        readonly System.Collections.Generic.HashSet<Health> alreadyHit = new();

        public void Launch(ProjectileData projectileData, Vector3 dir, Player owner)
        {
            data = projectileData;
            shooter = owner;
            direction = dir.normalized;
            traveled = 0f;
            alreadyHit.Clear();   // 관통탄: 이전 발사에서 맞힌 대상 목록을 비운다
        }

        public override void OnNetworkDespawn() => data = null;   // 풀에 있는 동안 움직이지 않게

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || data == null) return;

            float step = data.speed * Time.fixedDeltaTime;
            // 반지름만큼 뒤에서 검사를 시작한다: SphereCast는 시작 지점에서 이미 겹친 콜라이더를 무시하므로,
            // 경계에 걸친 대상을 다음 프레임에 놓치지 않게 한다
            Vector3 start = transform.position - direction * data.radius;
            if (data.pierce)
            {
                var hits = Physics.SphereCastAll(start, data.radius, direction, step + data.radius, data.hitMask);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    var health = Health.Of(h.collider);
                    if (health == null) { NetworkObject.Despawn(); return; }   // 지형에 닿으면 멈춘다
                    if (alreadyHit.Add(health)) health.Damage(data.damage, shooter);
                }
            }
            else if (Physics.SphereCast(start, data.radius, direction, out var hit, step + data.radius, data.hitMask))
            {
                Health.Of(hit.collider)?.Damage(data.damage, shooter);
                NetworkObject.Despawn();
                return;
            }

            transform.position += direction * step;
            traveled += step;
            if (traveled >= data.range) NetworkObject.Despawn();
        }
    }
}
