using UnityEngine;

namespace OnYourMark
{
    /// <summary>3번(스킬) 파츠 반환 각성 (P1): Q 나노 강화제 중 기본 참격을 하면 전방 관통 검기를 함께 발사한다.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Sword Wave")]
    public class SwordWaveAwakening : Awakening
    {
        public ProjectileData projectile;

        public override void OnBasicAttack(AwakeningSet owner)
        {
            var me = owner.Me;
            if (!me.Ultimate.IsActive) return;
            Vector3 dir = me.Ultimate.Aim;
            Vector3 origin = me.transform.position + Vector3.up * 1.2f + dir * 0.8f;
            var obj = Instantiate(projectile.prefab, origin, Quaternion.LookRotation(dir));
            obj.GetComponent<Projectile>().Launch(projectile, dir, me);
            obj.Spawn(destroyWithScene: true);
        }
    }
}
