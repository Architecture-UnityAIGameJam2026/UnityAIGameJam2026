using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// 지원 요청. Q 짧게: 가장 부족한 자원(체력 / 소모품) 요청, Q 길게: 휠에서 파츠 선택.
    /// HP나 소모품이 일정 비율 이하이면 서버가 자동 요청을 켠다. 요청은 지원을 받으면 꺼진다.
    /// </summary>
    public class SupportRequests : NetworkBehaviour
    {
        [SerializeField] SupportConfig config;
        [SerializeField] InputActionReference request, look;
        [Tooltip("휠이 열린 동안 끌 카메라 회전 입력")]
        [SerializeField] Behaviour[] lookInputs;

        readonly NetworkVariable<byte> manual = new();   // Q로 보낸 요청
        readonly NetworkVariable<byte> automatic = new(); // 15% 이하 자동 요청

        Player player;
        float pressTime;
        Vector2 wheelVector;

        public bool Has(HudPart part) => ((manual.Value | automatic.Value) & Bit(part)) != 0;
        public bool Any => (manual.Value | automatic.Value) != 0;

        // 휠 상태 (내 화면 전용)
        public bool WheelOpen { get; private set; }
        public HudPart? WheelChoice { get; private set; }

        void Awake() => player = GetComponent<Player>();

        void Update()
        {
            if (IsServer && IsSpawned) UpdateAutomatic();
            if (!IsOwner || !CursorLock.IsLocked) return;

            var q = request.action;
            if (q.WasPressedThisFrame()) pressTime = Time.time;
            if (q.IsPressed() && !WheelOpen && Time.time - pressTime >= config.wheelHoldTime) SetWheel(true);

            if (WheelOpen)
            {
                wheelVector += look.action.ReadValue<Vector2>();
                WheelChoice = PickPart(wheelVector);
            }

            if (q.WasReleasedThisFrame())
            {
                if (!WheelOpen) RequestRpc(-1);                          // 짧게: 자동 선택
                else if (WheelChoice is { } part) RequestRpc((int)part);
                SetWheel(false);
            }
        }

        void SetWheel(bool open)
        {
            WheelOpen = open;
            WheelChoice = null;
            wheelVector = Vector2.zero;
            foreach (var input in lookInputs) input.enabled = !open;
        }

        // 위: 1 생체, 오른쪽 아래: 2 지도, 왼쪽 아래: 3 소모품
        HudPart? PickPart(Vector2 v)
        {
            if (v.magnitude < config.wheelDeadzone) return null;
            float angle = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;   // 위쪽이 0도, 시계 방향
            if (angle > -60f && angle <= 60f) return HudPart.Bio;
            return angle > 60f ? HudPart.Map : HudPart.Consumable;
        }

        public void Request(HudPart part)
        {
            if (IsSpawned && IsOwner && (int)part >= 0 && (int)part < 3) RequestRpc((int)part);
        }

        [Rpc(SendTo.Server)]
        void RequestRpc(int part)
        {
            var p = part >= 0 ? (HudPart)part
                : player.Health.Ratio <= player.Ammo.Ratio ? HudPart.Bio : HudPart.Consumable;
            manual.Value |= Bit(p);
        }

        void UpdateAutomatic()
        {
            byte bits = 0;
            if (player.Health.Ratio <= config.autoRequestRatio) bits |= Bit(HudPart.Bio);
            if (player.Ammo.Ratio <= config.autoRequestRatio) bits |= Bit(HudPart.Consumable);
            if (automatic.Value != bits) automatic.Value = bits;
        }

        /// <summary>서버 전용: 지원을 받은 파츠의 요청을 끈다.</summary>
        public void Fulfill(HudPart part) => manual.Value &= (byte)~Bit(part);

        static byte Bit(HudPart part) => (byte)(1 << (int)part);
    }
}
