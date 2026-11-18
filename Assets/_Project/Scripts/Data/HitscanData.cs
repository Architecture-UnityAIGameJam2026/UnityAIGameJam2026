using UnityEngine;

namespace OnYourMark
{
    /// <summary>P2 레일건. 화면 중앙 조준 히트스캔.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Weapon/Hitscan")]
    public class HitscanData : ScriptableObject
    {
        public float damage = 100f;
        public float range = 50f;
        public float cooldown = 1.1f;
        public LayerMask hitMask;
        public AudioClip dryFireSound;
    }
}
