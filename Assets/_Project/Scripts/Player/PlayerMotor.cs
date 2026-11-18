using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// 이동 · 점프 · 호버링 · 대쉬. 수치는 전부 PlayerStats(SO)에서 읽는다.
    /// 카메라가 바라보는 방향 기준으로 움직이고, 몸은 항상 카메라 수평 방향을 바라본다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [SerializeField] PlayerStats stats;
        [SerializeField] InputActionReference move, jump, dash, zoom;

        CharacterController body;
        Transform cam;
        float verticalSpeed, hoverTime, dashTimeLeft, dashRecharge;
        Vector3 dashDirection;

        public PlayerStats Stats => stats;
        public int DashStacks { get; private set; }
        public bool IsZooming => stats.CanZoom && zoom.action.IsPressed();
        public float HoverLeft01 => 1f - hoverTime / stats.hoverMaxTime;

        void Awake()
        {
            body = GetComponent<CharacterController>();
            DashStacks = stats.dashMaxStacks;
        }

        void OnEnable()
        {
            move.action.actionMap.Enable();
            Cursor.lockState = CursorLockMode.Locked;
        }

        void OnDisable() => Cursor.lockState = CursorLockMode.None;

        void Update()
        {
            if (cam == null) cam = Camera.main.transform;
            float dt = Time.deltaTime;

            Vector3 input = CameraRelative(move.action.ReadValue<Vector2>());
            RechargeDash(dt);
            if (dash.action.WasPressedThisFrame()) TryDash(input);

            Vector3 step;
            if (dashTimeLeft > 0f)
            {
                // 대쉬 중에는 중력 무시. 마지막 프레임은 남은 시간만큼만 이동해 거리를 정확히 맞춘다
                float t = Mathf.Min(dt, dashTimeLeft);
                dashTimeLeft -= t;
                verticalSpeed = 0f;
                step = dashDirection * (stats.DashSpeed * t);
            }
            else
            {
                step = input * ((IsZooming ? stats.zoomMoveSpeed : stats.moveSpeed) * dt);
                step.y = VerticalStep(dt);
            }

            body.Move(step);
            transform.rotation = Quaternion.LookRotation(Flat(cam.forward));
        }

        /// <summary>이번 프레임의 수직 이동량. 프레임 간격이 길어도 점프 높이가 변하지 않도록 평균 속도로 적분한다.</summary>
        float VerticalStep(float dt)
        {
            if (body.isGrounded)
            {
                hoverTime = 0f;
                if (!jump.action.WasPressedThisFrame()) { verticalSpeed = -2f; return verticalSpeed * dt; }
                verticalSpeed = stats.JumpVelocity;
            }

            float before = verticalSpeed;
            verticalSpeed += stats.gravity * dt;

            // 호버링: 공중에서 Space를 누르고 있으면 낙하 속도를 hoverFallSpeed로 제한
            bool hovering = jump.action.IsPressed()
                            && verticalSpeed < -stats.hoverFallSpeed
                            && hoverTime < stats.hoverMaxTime;
            if (hovering)
            {
                verticalSpeed = -stats.hoverFallSpeed;
                hoverTime += dt;
                return verticalSpeed * dt;
            }
            return (before + verticalSpeed) * 0.5f * dt;
        }

        void TryDash(Vector3 input)
        {
            if (DashStacks == 0 || dashTimeLeft > 0f) return;
            DashStacks--;
            dashTimeLeft = stats.dashDuration;
            dashDirection = input.sqrMagnitude > 0.01f ? input.normalized : transform.forward;
        }

        // 스택은 하나씩 순서대로 충전
        void RechargeDash(float dt)
        {
            if (DashStacks >= stats.dashMaxStacks) { dashRecharge = 0f; return; }
            dashRecharge += dt;
            if (dashRecharge >= stats.dashCooldown) { DashStacks++; dashRecharge = 0f; }
        }

        Vector3 CameraRelative(Vector2 v) =>
            Vector3.ClampMagnitude(Flat(cam.forward) * v.y + Flat(cam.right) * v.x, 1f);

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }
    }
}
