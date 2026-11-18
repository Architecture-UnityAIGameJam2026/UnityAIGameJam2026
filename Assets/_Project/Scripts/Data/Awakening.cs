using System.Collections.Generic;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 각성 효과 하나 (전략 패턴). 자기 파츠를 돌려받으면 켜진다.
    /// 필요한 시점만 골라 재정의한다: 처치 시 / 기본 공격 시 / 명중 시 / 재장전 배율.
    /// 모든 훅은 서버에서 호출된다.
    /// </summary>
    public abstract class Awakening : ScriptableObject
    {
        public string displayName = "AWAKENING";
        public HudPart part;
        [Tooltip("발동 연출 (각 화면에서 잠깐 보였다 사라짐)")]
        public GameObject effect;
        public LayerMask enemyMask;

        public virtual void OnKill(AwakeningSet owner) { }
        public virtual void OnBasicAttack(AwakeningSet owner) { }
        public virtual void OnHit(AwakeningSet owner, Vector3 point) { }
        public virtual float ReloadMultiplier => 1f;

        /// <summary>origin에서 aim 방향 부채꼴(반각 halfAngle) 안, range 이내의 살아 있는 적.</summary>
        protected List<Health> EnemiesInCone(Vector3 origin, Vector3 aim, float range, float halfAngle)
        {
            var found = new List<Health>();
            Vector3 flatAim = new Vector3(aim.x, 0f, aim.z).normalized;
            foreach (var c in Physics.OverlapSphere(origin, range, enemyMask))
            {
                if (Health.Of(c) is not { IsDead: false } h || found.Contains(h)) continue;
                Vector3 to = h.transform.position - origin; to.y = 0f;
                if (Vector3.Angle(flatAim, to) <= halfAngle) found.Add(h);
            }
            return found;
        }

        protected List<Health> EnemiesAround(Vector3 center, float radius) => EnemiesInCone(center, Vector3.forward, radius, 180f);
    }
}
