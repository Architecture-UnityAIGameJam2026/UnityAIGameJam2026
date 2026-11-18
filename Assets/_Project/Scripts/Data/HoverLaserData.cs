using UnityEngine;

namespace OnYourMark
{
    /// <summary>공중 레이저형: 상공에서 거리 유지 -> 타겟 고정 후 조준선 차징 -> 직선 레이저.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Enemy/Hover Laser")]
    public class HoverLaserData : EnemyData
    {
        public float hoverHeight = 5.5f;
        public float preferredDistance = 14f;
        public float range = 35f;
        public float chargeTime = 2.5f;
        public float cooldown = 4f;
        public float damage = 25f;
        [Tooltip("차징 중 조준이 타겟을 따라가는 속도 (도/초). 낮을수록 피하기 쉽다")]
        public float trackingSpeed = 40f;
    }
}
