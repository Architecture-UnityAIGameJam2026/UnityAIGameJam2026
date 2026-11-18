using UnityEngine;

namespace OnYourMark
{
    /// <summary>근접 자폭형: 추적 -> 범위 안에서 정지 -> 카운트다운 -> 폭발.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Enemy/Rusher")]
    public class RusherData : EnemyData
    {
        public float triggerRange = 2.5f;
        public float fuseTime = 1.5f;
        public float blastRadius = 3.5f;
        public float blastDamage = 35f;
    }
}
