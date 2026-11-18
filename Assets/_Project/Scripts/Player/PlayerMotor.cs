using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// 이동 · 점프 · 추진 비행(호버) · 대쉬. 수치는 전부 PlayerStats(SO)에서 읽는다.
    /// 카메라가 바라보는 방향 기준으로 움직이고, 몸은 항상 카메라 수평 방향을 바라본다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [SerializeField] PlayerStats stats;
        [SerializeField] InputActionReference move, jump, dash, zoom;
        [Tooltip("이 높이 아래로 떨어지면 마지막으로 서 있던 자리로 돌아온다 (맵 밖으로 날아간 경우)")]
        [SerializeField] float fallLimitY = -20f;

        CharacterController body;
        PlayerBuffs buffs;
        Transform cam;
        float verticalSpeed, hoverTime, groundedTime, dashTimeLeft, dashRecharge;
        bool flying, jumpPressedInAir;
        int bonusDashStacks;
        Vector3 dashDirection, lastGroundedPosition;
        float infiniteDashUntil;   // 이 시각(Time.time)까지 대쉬가 칸을 쓰지 않는다

        /// <summary>대쉬를 시작할 때 (내 클라이언트)</summary>
        public event System.Action Dashed;
        public bool InfiniteDash => Time.time < infiniteDashUntil;

        public PlayerStats Stats => stats;
        public int DashStacks { get; private set; }
        /// <summary>지금의 최대 대쉬 횟수 (기본 + 스킬로 늘어난 칸)</summary>
        public int DashCapacity => stats.dashMaxStacks + bonusDashStacks;
        public bool IsZooming => stats.CanZoom && ZoomAllowed && Held(zoom);
        /// <summary>지금 무기가 줌을 쓸 수 있는지 (P2 레일건 모드에서만)</summary>
        public bool ZoomAllowed { get; set; } = true;
        /// <summary>대쉬 금지 (P2 무기 변형 중)</summary>
        public bool DashLocked { get; set; }
        /// <summary>0 이상이면 이동 속도를 이 값으로 고정 (P2 무기 변형 중 감속)</summary>
        public float SpeedOverride { get; set; } = -1f;
        /// <summary>궁극기 등에서 곱하는 이동 속도 배율 (비행 포함)</summary>
        public float ExternalSpeedMultiplier { get; set; } = 1f;
        public bool IsFlying => flying;
        /// <summary>대쉬가 시간이 지나면 저절로 차는지. 2번 파츠를 돌려받기 전에는 꺼진다 (ResourceReload)</summary>
        public bool AutoRechargeDash { get; set; } = true;
        public float HoverLeft01 => 1f - hoverTime / stats.hoverMaxTime;

        // 커서가 풀려 있으면(Esc) 조작 입력을 무시한다. 중력은 계속 적용된다
        static bool Pressed(InputActionReference a) => CursorLock.IsLocked && a.action.WasPressedThisFrame();
        static bool Held(InputActionReference a) => CursorLock.IsLocked && a.action.IsPressed();

        void Awake()
        {
            body = GetComponent<CharacterController>();
            buffs = GetComponent<PlayerBuffs>();
            DashStacks = stats.dashMaxStacks;
            lastGroundedPosition = transform.position;
        }

        void OnEnable()
        {
            move.action.actionMap.Enable();
            // 네트워크 스폰은 transform만 스폰 위치로 옮긴다. CharacterController가 생성 위치(원점)를
            // 기억한 채 첫 Move를 하면 원점으로 되돌아가므로, 켜질 때 물리 위치를 맞춰 준다
            Physics.SyncTransforms();
        }

        void Update()
        {
            if (cam == null) cam = Camera.main.transform;
            float dt = Time.deltaTime;

            Vector2 stick = CursorLock.IsLocked ? move.action.ReadValue<Vector2>() : Vector2.zero;
            Vector3 input = CameraRelative(stick);
            RechargeDash(dt);
            RechargeHover(dt);
            if (Pressed(dash)) TryDash(input);

            Vector3 step;
            if (dashTimeLeft > 0f)
            {
                // 대쉬 중에는 중력 무시. 마지막 프레임은 남은 시간만큼만 이동해 거리를 정확히 맞춘다
                float t = Mathf.Min(dt, dashTimeLeft);
                dashTimeLeft -= t;
                verticalSpeed = 0f;
                flying = false;
                step = dashDirection * (stats.DashSpeed * t);
            }
            else if (UpdateFlight(dt))
            {
                // 추진 비행: 중력 없이 카메라가 바라보는 3D 방향으로 날아간다 (위를 보면 상승, 아래를 보면 급강하)
                step = cam.forward * (stats.hoverSpeed * buffs.SpeedMultiplier * ExternalSpeedMultiplier * dt);
            }
            else
            {
                float speed = SpeedOverride >= 0f ? SpeedOverride : IsZooming ? stats.zoomMoveSpeed : stats.moveSpeed;
                step = input * (speed * buffs.SpeedMultiplier * ExternalSpeedMultiplier * dt);
                step.y = VerticalStep(dt);
            }

            body.Move(step);
            RecoverFromFall();
        }

        void RecoverFromFall()
        {
            if (body.isGrounded) lastGroundedPosition = transform.position;
            if (transform.position.y >= fallLimitY) return;
            transform.position = lastGroundedPosition + Vector3.up * 0.5f;
            verticalSpeed = 0f;
            flying = false;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 공중에서 Space를 누르고 있으면 비행. 점프한 그 입력으로 바로 날지 않도록,
        /// 점프 정점을 지났거나(하강 중) 공중에서 Space를 새로 눌렀을 때부터 시작한다.
        /// </summary>
        bool UpdateFlight(float dt)
        {
            bool airborne = !body.isGrounded;
            if (!airborne) jumpPressedInAir = false;
            else if (Pressed(jump)) jumpPressedInAir = true;

            bool hasFuel = hoverTime < stats.hoverMaxTime;
            bool canStart = airborne && (verticalSpeed <= 0f || jumpPressedInAir);
            flying = Held(jump) && hasFuel && airborne && (flying || canStart);

            if (!flying) return false;
            hoverTime += dt;
            verticalSpeed = 0f;   // 비행이 끝나면 그 자리에서부터 다시 떨어진다
            return true;
        }

        /// <summary>착지 후 hoverRechargeDelay초가 지나면 비행 게이지를 다시 채운다.</summary>
        void RechargeHover(float dt)
        {
            groundedTime = body.isGrounded ? groundedTime + dt : 0f;
            if (groundedTime < stats.hoverRechargeDelay) return;
            hoverTime = stats.hoverRechargeRate <= 0f
                ? 0f
                : Mathf.Max(0f, hoverTime - stats.hoverRechargeRate * dt);
        }

        /// <summary>이번 프레임의 수직 이동량. 프레임 간격이 길어도 점프 높이가 변하지 않도록 평균 속도로 적분한다.</summary>
        float VerticalStep(float dt)
        {
            if (body.isGrounded)
            {
                if (!Pressed(jump)) { verticalSpeed = -2f; return verticalSpeed * dt; }
                verticalSpeed = stats.JumpVelocity;
            }

            float before = verticalSpeed;
            verticalSpeed += stats.gravity * dt;
            return (before + verticalSpeed) * 0.5f * dt;
        }

        /// <summary>대쉬 스택을 모두 채운다. infiniteSeconds 동안은 대쉬해도 칸이 줄지 않는다 (오버차지).</summary>
        public void ResetDash(float infiniteSeconds = 0f)
        {
            DashStacks = DashCapacity;
            dashRecharge = 0f;
            if (infiniteSeconds > 0f) infiniteDashUntil = Time.time + infiniteSeconds;
        }

        /// <summary>최대 대쉬 횟수를 기본값 + bonus로 바꾼다 (P1 나노 강화제). fill이면 전부 채운다.</summary>
        public void SetDashBonus(int bonus, bool fill)
        {
            bonusDashStacks = Mathf.Max(0, bonus);
            DashStacks = fill ? DashCapacity : Mathf.Min(DashStacks, DashCapacity);
            if (fill) dashRecharge = 0f;
        }

        /// <summary>대쉬 칸을 amount만큼 즉시 채운다 (최대치까지). 패링 성공 보상 등.</summary>
        public void AddDashStacks(int amount) => DashStacks = Mathf.Min(DashCapacity, DashStacks + amount);

        void TryDash(Vector3 input)
        {
            if ((DashStacks == 0 && !InfiniteDash) || dashTimeLeft > 0f || DashLocked) return;
            if (!InfiniteDash) DashStacks--;
            Dashed?.Invoke();
            dashTimeLeft = stats.dashDuration;
            dashDirection = input.sqrMagnitude > 0.01f ? input.normalized : transform.forward;
        }

        // 스택은 하나씩 순서대로 충전 (자동 충전이 허용됐을 때만)
        void RechargeDash(float dt)
        {
            if (DashStacks >= DashCapacity || !AutoRechargeDash) { dashRecharge = 0f; return; }
            dashRecharge += dt;
            if (dashRecharge >= stats.dashCooldown) { DashStacks++; dashRecharge = 0f; }
        }

        Vector3 CameraRelative(Vector2 v) =>
            Vector3.ClampMagnitude(Flat(cam.forward) * v.y + Flat(cam.right) * v.x, 1f);

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }
    }
}
