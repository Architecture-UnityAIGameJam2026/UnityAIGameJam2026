using System.Collections.Generic;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// P2 Q — 전술 유도 사격: 시야 중앙 원뿔 안의 적을 자동으로 노린다. 잔탄을 쓰지 않는다.
    /// - 따발총 모드: 락온된 적들에게 돌아가며 초당 rate발 유도탄 (발당 arDamage)
    /// - 레일건 모드: 조준선에 가장 가까운 적 하나에 관통 레일탄 railShots발 (발당 railDamage)
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Ultimate/Smart Lock-on")]
    public class SmartLockOnUltimate : UltimateData
    {
        [Tooltip("락온 원뿔 전체 각도")]
        public float coneAngle = 40f;
        public float range = 60f;
        public LayerMask enemyMask;
        public LayerMask obstacleMask;

        [Header("따발총 모드")]
        public float rate = 15f;
        public float arDamage = 22f;

        [Header("레일건 모드")]
        public int railShots = 3;
        public float railInterval = 0.3f;
        public float railDamage = 180f;

        public override void Tick(Ultimate u, float dt)
        {
            var modes = u.Me.GetComponent<WeaponModes>();
            bool rail = modes != null && modes.Current.pierce;
            u.timer += dt;

            if (rail)
            {
                if (u.counter >= railShots || u.timer < railInterval) return;
                var target = Closest(u);
                if (target == null) return;
                u.timer = 0f; u.counter++;
                Fire(u, target, railDamage);
            }
            else
            {
                float interval = 1f / (rate * u.HomingRateMultiplier);
                while (u.timer >= interval)
                {
                    u.timer -= interval;
                    var targets = Locked(u);
                    if (targets.Count == 0) { u.timer = 0f; return; }
                    Fire(u, targets[u.counter++ % targets.Count], arDamage);   // 락온된 적들에게 돌아가며
                }
            }
        }

        static Vector3 Eye(Ultimate u) => u.transform.position + Vector3.up * 1.6f;

        List<Health> Locked(Ultimate u)
        {
            var found = new List<Health>();
            Vector3 eye = Eye(u);
            foreach (var c in Physics.OverlapSphere(eye, range, enemyMask))
            {
                if (Health.Of(c) is not { IsDead: false } h || found.Contains(h)) continue;
                Vector3 to = Chest(h) - eye;
                if (Vector3.Angle(u.Aim, to) > coneAngle * 0.5f) continue;
                if (Physics.Raycast(eye, to.normalized, to.magnitude - 0.5f, obstacleMask)) continue;   // 벽 뒤는 제외
                found.Add(h);
            }
            return found;
        }

        Health Closest(Ultimate u)
        {
            Health best = null; float bestAngle = float.MaxValue; Vector3 eye = Eye(u);
            foreach (var h in Locked(u))
            {
                float a = Vector3.Angle(u.Aim, Chest(h) - eye);
                if (a < bestAngle) { bestAngle = a; best = h; }
            }
            return best;
        }

        static Vector3 Chest(Health h) => h.transform.position + Vector3.up * 0.9f;

        void Fire(Ultimate u, Health target, float damage)
        {
            Vector3 from = Eye(u) + u.Aim * 0.5f;
            target.Damage(damage, u.Me);
            u.ShowShot(from, Chest(target));
        }
    }
}
