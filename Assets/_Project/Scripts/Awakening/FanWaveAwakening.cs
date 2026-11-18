using UnityEngine;

namespace OnYourMark
{
    /// <summary>P1 소모품 각성: 기본 공격마다 전방 부채꼴 검기가 함께 나간다.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Fan Wave")]
    public class FanWaveAwakening : Awakening
    {
        public float range = 18f;
        public float halfAngle = 30f;
        public float damage = 50f;

        public override void OnBasicAttack(AwakeningSet owner)
        {
            var t = owner.transform;
            foreach (var enemy in EnemiesInCone(t.position, t.forward, range, halfAngle)) enemy.Damage(damage, owner.Me);
            owner.PlayEffect(this, t.position + Vector3.up + t.forward * (range * 0.5f), t.rotation, 1f);
        }
    }
}
