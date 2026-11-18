using System.Collections.Generic;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// P2 Q — 전술 유도 사격: 시야 중앙 원뿔 안의 적을 자동으로 노린다. 잔탄을 쓰지 않는다.
    /// - 따발총 모드: 락온된 적들에게 돌아가며 초당 rate발 유도탄 (발당 arDamage)
    /// - 레일건 모드: 조준선에 가장 가까운 적 하나에 관통 레일탄 railShots발 (발당 railDamage)
    ///   3번 파츠 반환 각성(RailOverdrive)이 있으면 대신 유도 폭발 레이저 한 발 (표적 + 반경 폭발)
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

            if (rail && u.Awakening.Find<RailOverdriveAwakening>() is { } overdrive)
            {
                if (u.counter > 0) return;   // 한 발
                var target = Closest(u);
                if (target == null) return;
                u.counter = 1;
                Overdrive(u, target, overdrive);
            }
            else if (rail)
            {
                if (u.counter >= railShots || u.timer < railInterval) return;
                var target = Closest(u);
                if (target == null) return;
                u.timer = 0f; u.counter++;
                Fire(u, target, railDamage);
            }
            else
            {
                float interval = 1f / rate;
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

        // 표적을 끝까지 따라가 맞히고, 착탄 지점 주변을 폭발로 쓸어버린다
        void Overdrive(Ultimate u, Health target, RailOverdriveAwakening o)
        {
            Vector3 impact = Chest(target);
            Fire(u, target, o.laserDamage);
            var hit = new HashSet<Health> { target };
            foreach (var c in Physics.OverlapSphere(impact, o.blastRadius, enemyMask))
                if (Health.Of(c) is { IsDead: false } h && hit.Add(h)) h.Damage(o.blastDamage, u.Me);
            u.ShowBlast(impact, o.blastRadius);
        }

        void Fire(Ultimate u, Health target, float damage)
        {
            Vector3 from = Eye(u) + u.Aim * 0.5f;
            target.Damage(damage, u.Me);
            u.ShowShot(from, Chest(target));
        }
    }
}
