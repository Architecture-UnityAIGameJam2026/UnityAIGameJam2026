using UnityEngine;

namespace OnYourMark
{
    /// <summary>P1 근접 참격. 전방 박스 범위 판정.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Weapon/Melee")]
    public class MeleeData : ScriptableObject
    {
        public float damage = 50f;
        public float windup = 0.1f;
        public float recovery = 0.3f;
        [Tooltip("판정 박스 크기 (폭, 높이, 전방 길이)")]
        public Vector3 boxSize = new(2.5f, 2f, 3f);
        public LayerMask hitMask;
    }
}
