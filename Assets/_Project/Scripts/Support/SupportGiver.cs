using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// 원격 체력 지원. 팀원을 조준한 채 트리거(P1 우클릭 / P2 좌클릭)를 누르면 바로 회복시켜 준다.
    /// 요청 아이콘 같은 조건은 없고, 팀원의 1번 파츠를 아직 들고 있을 때(반환 전)만, 쿨타임마다 쓸 수 있다.
    /// 팀원을 조준하지 않았으면 원래 동작(P1 월드 핑 / P2 사격)으로 처리된다.
    /// </summary>
    [DefaultExecutionOrder(-10)]   // 같은 프레임에 무기보다 먼저 입력을 가져간다
    public class SupportGiver : NetworkBehaviour
    {
        [SerializeField] SupportConfig config;
        [SerializeField] InputActionReference trigger;
        [Tooltip("팀원을 조준하지 않았을 때 트리거로 월드 핑을 찍는다 (P1)")]
        [SerializeField] bool pingWhenIdle;

        readonly NetworkVariable<double> readyAt = new();   // 쿨타임 종료 시각 (서버 시간)

        Player me;
        CrossHudState hud;

        /// <summary>이번 프레임 트리거 입력을 지원으로 썼는지. 무기는 true면 쏘지 않는다.</summary>
        public bool ConsumedTrigger { get; private set; }

        public float CooldownLeft => IsSpawned ? Mathf.Max(0f, (float)(readyAt.Value - NetworkManager.ServerTime.Time)) : 0f;

        /// <summary>지금 체력 지원을 줄 수 있는 상태인지 (파트너 파츠를 들고 있음).</summary>
        public bool CanSupport => hud != null && !hud.IsReturned(HudPart.Bio) && me.Partner != null;

        public override void OnNetworkSpawn()
        {
            me = GetComponent<Player>();
            hud = FindFirstObjectByType<CrossHudState>();
        }

        void Update()
        {
            ConsumedTrigger = false;
            if (!IsOwner || !CursorLock.IsLocked || !trigger.action.WasPressedThisFrame()) return;

            var partner = me.Partner;
            if (CanSupport && !partner.Health.IsDead && IsAimedAt(partner))
            {
                ConsumedTrigger = true;
                SupportRpc();
            }
            else if (pingWhenIdle && AimPoint(out var point))
            {
                PingRpc(point);
            }
        }

        // 소프트 락온: 조준선(화면 중앙 레이)과 팀원 가슴 사이 거리가 반경 이내
        bool IsAimedAt(Player partner)
        {
            var cam = Camera.main.transform;
            Vector3 chest = partner.transform.position + Vector3.up * 1.1f;
            float along = Vector3.Dot(chest - cam.position, cam.forward);
            if (along <= 0f || along > config.supportRange) return false;
            return Vector3.Distance(chest, cam.position + cam.forward * along) <= config.softLockRadius;
        }

        bool AimPoint(out Vector3 point)
        {
            var cam = Camera.main.transform;
            bool hit = Physics.Raycast(cam.position, cam.forward, out var info, config.supportRange, config.pingMask);
            point = info.point;
            return hit;
        }

        [Rpc(SendTo.Server)]
        void SupportRpc()
        {
            var target = me.Partner;
            if (!CanSupport || target.Health.IsDead || CooldownLeft > 0f) return;

            var effect = target.Stats.SupportFor(HudPart.Bio);
            if (effect == null) return;
            effect.Apply(me, target);
            readyAt.Value = NetworkManager.ServerTime.Time + effect.cooldown;
        }

        [Rpc(SendTo.Server)]
        void PingRpc(Vector3 point) => ShowPingRpc(point);

        [Rpc(SendTo.ClientsAndHost)]
        void ShowPingRpc(Vector3 point) => Destroy(Instantiate(config.pingPrefab, point, Quaternion.identity), config.pingDuration);
    }
}
