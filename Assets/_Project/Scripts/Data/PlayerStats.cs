using UnityEngine;

namespace OnYourMark
{
    /// <summary>포지션(P1 Sword / P2 Railgun)별 이동 수치. 기획서 1장 표를 그대로 옮긴 데이터.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Player Stats")]
    public class PlayerStats : ScriptableObject
    {
        [Header("HUD 표시")]
        public string displayName = "P1";
        public Color color = Color.white;
        [Tooltip("소모품 HUD에 탄창 대신 대쉬 횟수를 표시 (P1 검)")]
        public bool showDashOnHud;

        [Header("이동 (m/s)")]
        public float moveSpeed = 7.5f;
        [Tooltip("우클릭 줌 중 이동 속도. 0이면 줌 없음")]
        public float zoomMoveSpeed;
        [Tooltip("줌 배율. 1이면 줌 없음")]
        public float zoomMagnification = 1f;

        [Header("점프 / 중력")]
        public float jumpHeight = 2.2f;
        public float gravity = -22f;

        [Header("추진 비행 (공중에서 Space 홀드)")]
        [Tooltip("카메라가 바라보는 방향으로 날아가는 속도 (m/s)")]
        public float hoverSpeed = 9f;
        [Tooltip("한 번에 날 수 있는 최대 시간 (게이지)")]
        public float hoverMaxTime = 2.5f;
        [Tooltip("착지 후 이 시간이 지나야 게이지가 충전된다")]
        public float hoverRechargeDelay = 1.5f;
        [Tooltip("초당 충전되는 비행 시간. 0이면 지연 시간이 지나는 순간 한 번에 가득 찬다")]
        public float hoverRechargeRate = 0f;

        [Header("대쉬 (Shift)")]
        public float dashDistance = 9f;
        public float dashDuration = 0.2f;
        public float dashCooldown = 3.5f;
        public int dashMaxStacks = 2;

        [Header("소모품 자원 (P1 검기 칸 / P2 탄창)")]
        public int resourceCapacity = 4;
        public bool resourceStartsFull;

        [Header("원격 지원")]
        [Tooltip("상대가 내 1번 파츠를 들고 있을 때, 상대가 나를 조준해 주는 지원 효과 (현재는 체력 지원만)")]
        public SupportEffect[] supportEffects;

        [Header("각성")]
        [Tooltip("내 파츠를 돌려받았을 때 켜지는 각성 효과 (파츠당 하나)")]
        public Awakening[] awakenings;

        public SupportEffect SupportFor(HudPart part)
        {
            foreach (var e in supportEffects) if (e.part == part) return e;
            return null;
        }

        public float JumpVelocity => Mathf.Sqrt(2f * -gravity * jumpHeight);
        public float DashSpeed => dashDistance / dashDuration;
        public bool CanZoom => zoomMagnification > 1f;
    }
}
