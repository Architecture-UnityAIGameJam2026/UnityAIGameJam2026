using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>서버에서 직선으로 날아가며 앞쪽을 SphereCast로 검사한다. 적중하거나 사거리를 다 날면 사라진다.</summary>
    public class Projectile : NetworkBehaviour
    {
        ProjectileData data;
        Vector3 direction;
        float traveled;

        public void Launch(ProjectileData projectileData, Vector3 dir)
        {
            data = projectileData;
            direction = dir.normalized;
        }

        void FixedUpdate()
        {
            if (!IsServer || data == null) return;

            float step = data.speed * Time.fixedDeltaTime;
            // 반지름만큼 뒤에서 검사를 시작한다: SphereCast는 시작 지점에서 이미 겹친 콜라이더를 무시하므로,
            // 경계에 걸친 대상을 다음 프레임에 놓치지 않게 한다
            Vector3 start = transform.position - direction * data.radius;
            if (Physics.SphereCast(start, data.radius, direction, out var hit, step + data.radius, data.hitMask))
            {
                Health.Of(hit.collider)?.Damage(data.damage);
                NetworkObject.Despawn();
                return;
            }

            transform.position += direction * step;
            traveled += step;
            if (traveled >= data.range) NetworkObject.Despawn();
        }
    }
}
