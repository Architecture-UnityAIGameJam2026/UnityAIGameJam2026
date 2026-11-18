using UnityEngine;

namespace OnYourMark
{
    /// <summary>P1 E 스킬 — 튕겨내기(패링) 수치.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Weapon/Parry")]
    public class ParryData : ScriptableObject
    {
        public float duration = 1.5f;
        public float cooldown = 8f;
        [Tooltip("막는 범위: 정면 기준 이 각도(좌우 반각) 안에서 오는 공격만 막는다")]
        public float frontHalfAngle = 75f;
        [Tooltip("정면 방어막 크기 (폭, 높이, 전방 길이) — 표시 · 투사체 판정용")]
        public Vector3 boxSize = new(2.2f, 2f, 1.5f);

        [Header("반사 (투사체 · 레이저)")]
        public float reflectDamage = 120f;
        public float reflectRange = 60f;
        public LayerMask reflectMask;

        [Header("근접 방어")]
        [Tooltip("근접 공격을 막았을 때 공격한 적의 경직 시간")]
        public float stunSeconds = 1.5f;

        [Header("성공 보상")]
        [Tooltip("방어 · 반사에 성공하면 돌려받는 대쉬 칸 (시전당 1회)")]
        public int dashPayback = 1;

    }
}
