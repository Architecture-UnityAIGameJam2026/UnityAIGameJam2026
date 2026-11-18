using UnityEngine;

namespace OnYourMark
{
    /// <summary>P1 지도 각성 (F): 전방의 적 무리 한가운데로 순간이동해 주변을 벤다.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Blink Slash")]
    public class BlinkSlashAwakening : Awakening
    {
        public float searchRange = 10f;
        public float searchHalfAngle = 45f;
        public float slashRadius = 3f;
        public float damage = 50f;

        public override bool Activate(AwakeningSet owner, Vector3 aim)
        {
            var targets = EnemiesInCone(owner.transform.position, aim, searchRange, searchHalfAngle);
            if (targets.Count == 0) return false;

            Vector3 center = Vector3.zero;
            foreach (var t in targets) center += t.transform.position;
            center /= targets.Count;
            center.y = owner.transform.position.y;   // 공중 적이 섞여도 같은 높이로

            owner.TeleportRpc(center);
            foreach (var enemy in EnemiesAround(center, slashRadius)) enemy.Damage(damage, owner.Me);
            owner.PlayEffect(this, center + Vector3.up, Quaternion.identity, slashRadius * 2f);
            return true;
        }
    }
}
