using UnityEngine;

namespace OnYourMark
{
    /// <summary>P2 히트스캔 무기 모드 하나 (따발총 / 레일건). 화면 중앙 조준.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Weapon/Hitscan")]
    public class HitscanData : ScriptableObject
    {
        public string displayName = "RAIL";
        public float damage = 100f;
        public float range = 50f;
        [Tooltip("발사 간격 (초)")]
        public float cooldown = 1.1f;
        [Tooltip("장탄수")]
        public int magazine = 6;
        [Tooltip("좌클릭을 누르고 있으면 계속 쏜다 (연사)")]
        public bool automatic;
        [Tooltip("맞은 적을 모두 관통한다 (지형에서 멈춤)")]
        public bool pierce;
        [Tooltip("우클릭 줌 사용 가능")]
        public bool allowZoom;
        public LayerMask hitMask;
        public AudioClip dryFireSound;
    }
}
