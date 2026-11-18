using UnityEngine;

namespace OnYourMark
{
    /// <summary>P2 소모품 각성: 재장전 시간 단축 + 명중 지점 범위 폭발.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Explosive Rounds")]
    public class ExplosiveRoundsAwakening : Awakening
    {
        [Range(0.1f, 1f)] public float reloadTimeMultiplier = 0.5f;
        public float radius = 3f;
        public float damage = 50f;

        public override float ReloadMultiplier => reloadTimeMultiplier;

        public override void OnHit(AwakeningSet owner, Vector3 point)
        {
            foreach (var enemy in EnemiesAround(point, radius)) enemy.Damage(damage, owner.Me);
            owner.PlayEffect(this, point, Quaternion.identity, radius * 2f);
        }
    }
}
