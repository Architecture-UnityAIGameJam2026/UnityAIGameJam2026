using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// 원격 지원. 요청을 켠 팀원을 조준한 채 트리거(P1 우클릭 / P2 좌클릭)를 누르면,
    /// 내가 들고 있는 팀원 파츠 중 요청된 것의 지원 효과를 서버가 적용한다.
    /// 팀원을 조준하지 않았으면 원래 동작(P1 월드 핑 / P2 사격)으로 처리된다.
    /// </summary>
    [DefaultExecutionOrder(-10)]   // 같은 프레임에 무기보다 먼저 입력을 가져간다
    public class SupportGiver : NetworkBehaviour
    {
        [SerializeField] SupportConfig config;
        [SerializeField] InputActionReference trigger;
        [Tooltip("팀원을 조준하지 않았을 때 트리거로 월드 핑을 찍는다 (P1)")]
        [SerializeField] bool pingWhenIdle;

        readonly NetworkList<double> readyAt = new();   // 파츠별 쿨타임 종료 시각 (서버 시간)

        Player me;
        CrossHudState hud;

        /// <summary>이번 프레임 트리거 입력을 지원으로 썼는지. 무기는 true면 쏘지 않는다.</summary>
        public bool ConsumedTrigger { get; private set; }

        public float CooldownLeft(HudPart part) =>
            readyAt.Count > (int)part ? Mathf.Max(0f, (float)(readyAt[(int)part] - NetworkManager.ServerTime.Time)) : 0f;

        public override void OnNetworkSpawn()
        {
            me = GetComponent<Player>();
            hud = FindFirstObjectByType<CrossHudState>();
            if (IsServer) for (int i = 0; i < 3; i++) readyAt.Add(0);
        }

        void Update()
        {
            ConsumedTrigger = false;
            if (!IsOwner || !NetworkObject.IsPlayerObject || !CursorLock.IsLocked || !trigger.action.WasPressedThisFrame()) return;

            var partner = me.Partner;
            if (partner != null && partner.Requests.Any && IsAimedAt(partner))
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
            if (target == null) return;
            double now = NetworkManager.ServerTime.Time;

            for (int i = 0; i < 3; i++)
            {
                var part = (HudPart)i;
                // 요청이 켜져 있고, 내가 그 파츠를 아직 들고 있고(반환 전), 쿨타임이 끝났을 때만
                if (!target.Requests.Has(part) || hud.IsReturned(part) || now < readyAt[i]) continue;

                Give(part, target, now);
            }
        }

        public bool BotGive(HudPart part)
        {
            var target = me.Partner;
            if (!IsServer || !me.IsBot || !target || target.Health.IsDead || !target.Requests.Has(part) || hud.IsReturned(part) || CooldownLeft(part) > 0 || Vector3.Distance(transform.position,target.transform.position) > config.supportRange) return false;
            Give(part, target, NetworkManager.ServerTime.Time);
            return true;
        }

        void Give(HudPart part, Player target, double now)
        {
            int i = (int)part;
            var effect = target.Stats.SupportFor(part);
            effect.Apply(me, target);
            readyAt[i] = now + effect.cooldown;
            target.Requests.Fulfill(part);
            me.ReportAction(part == HudPart.Bio ? "grant_bio" : part == HudPart.Map ? "share_map" : "grant_resource", part.ToString());
        }

        [Rpc(SendTo.Server)]
        void PingRpc(Vector3 point) => ShowPingRpc(point);

        [Rpc(SendTo.ClientsAndHost)]
        void ShowPingRpc(Vector3 point) => Destroy(Instantiate(config.pingPrefab, point, Quaternion.identity), config.pingDuration);
    }
}
