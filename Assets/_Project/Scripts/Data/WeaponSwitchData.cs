using UnityEngine;

namespace OnYourMark
{
    /// <summary>P2 E 스킬 — 무기 모드 전환 수치.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Weapon/Weapon Switch")]
    public class WeaponSwitchData : ScriptableObject
    {
        [Tooltip("변형 선딜레이: 이 동안 사격 · 줌 · 대쉬 불가")]
        public float windup = 1.8f;
        [Tooltip("전환이 끝난 뒤 다시 전환할 수 있을 때까지")]
        public float cooldown = 2f;
        [Tooltip("변형 중 이동 속도 (m/s)")]
        public float moveSpeedWhileSwitching = 3f;
    }
}
