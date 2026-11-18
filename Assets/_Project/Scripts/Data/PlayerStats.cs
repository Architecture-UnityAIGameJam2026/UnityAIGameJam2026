using UnityEngine;

namespace OnYourMark
{
    /// <summary>포지션(P1 Sword / P2 Railgun)별 이동 수치. 기획서 1장 표를 그대로 옮긴 데이터.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Player Stats")]
    public class PlayerStats : ScriptableObject
    {
        [Header("이동 (m/s)")]
        public float moveSpeed = 7.5f;
        [Tooltip("우클릭 줌 중 이동 속도. 0이면 줌 없음")]
        public float zoomMoveSpeed;
        [Tooltip("줌 배율. 1이면 줌 없음")]
        public float zoomMagnification = 1f;

        [Header("점프 / 중력")]
        public float jumpHeight = 2.2f;
        public float gravity = -22f;

        [Header("호버링 (Space 홀드)")]
        public float hoverMaxTime = 2.5f;
        public float hoverFallSpeed = 1.5f;

        [Header("대쉬 (Shift)")]
        public float dashDistance = 9f;
        public float dashDuration = 0.2f;
        public float dashCooldown = 3.5f;
        public int dashMaxStacks = 2;

        [Header("소모품 자원 (P1 검기 칸 / P2 탄창)")]
        public int resourceCapacity = 4;
        public bool resourceStartsFull;

        public float JumpVelocity => Mathf.Sqrt(2f * -gravity * jumpHeight);
        public float DashSpeed => dashDistance / dashDuration;
        public bool CanZoom => zoomMagnification > 1f;
    }
}
