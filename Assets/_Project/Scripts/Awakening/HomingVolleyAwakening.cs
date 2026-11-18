using UnityEngine;

namespace OnYourMark
{
    /// <summary>P2 지도 각성 (F): 전방 부채꼴을 스캔해 여러 적에게 동시에 유도탄을 꽂는다.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Homing Volley")]
    public class HomingVolleyAwakening : Awakening
    {
        public float scanRange = 30f;
        public float scanHalfAngle = 35f;
        public int maxTargets = 5;
        public float damage = 100f;

        public override bool Activate(AwakeningSet owner, Vector3 aim)
        {
            var targets = EnemiesInCone(owner.transform.position, aim, scanRange, scanHalfAngle);
            if (targets.Count == 0) return false;

            targets.Sort((a, b) => Distance(owner, a).CompareTo(Distance(owner, b)));
            for (int i = 0; i < Mathf.Min(maxTargets, targets.Count); i++)
            {
                owner.PlayEffect(this, targets[i].transform.position + Vector3.up, Quaternion.identity, 1f);
                targets[i].Damage(damage, owner.Me);
            }
            return true;
        }

        static float Distance(AwakeningSet owner, Health h) => Vector3.Distance(owner.transform.position, h.transform.position);
    }
}
