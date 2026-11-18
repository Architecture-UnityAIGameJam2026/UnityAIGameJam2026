using UnityEngine;

namespace OnYourMark
{
    /// <summary>요청 · 원격 지원 공통 수치.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Support/Config")]
    public class SupportConfig : ScriptableObject
    {
        [Header("요청 (Q)")]
        [Tooltip("이 시간 이상 누르고 있으면 선택 휠을 연다")]
        public float wheelHoldTime = 0.25f;
        [Tooltip("휠에서 이만큼 마우스를 움직여야 항목이 선택된다")]
        public float wheelDeadzone = 40f;
        [Tooltip("HP 또는 소모품이 이 비율 이하이면 자동 요청")]
        [Range(0, 1)] public float autoRequestRatio = 0.15f;

        [Header("지원 판정")]
        [Tooltip("조준선과 팀원 사이 거리가 이 이내면 팀원을 조준한 것으로 본다 (소프트 락온)")]
        public float softLockRadius = 1.5f;
        public float supportRange = 60f;

        [Header("월드 핑")]
        public GameObject pingPrefab;
        public float pingDuration = 3f;
        public LayerMask pingMask;
    }
}
