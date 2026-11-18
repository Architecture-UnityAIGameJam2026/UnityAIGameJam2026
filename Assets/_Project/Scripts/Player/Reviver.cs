using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// 팀원 소생. 쓰러진 팀원 반경 안에서 E를 누르고 있으면 진행도가 차고,
    /// 다 차면 팀원이 최대 HP의 일정 비율로 일어난다. 떼거나 멀어지면 처음부터.
    /// </summary>
    public class Reviver : NetworkBehaviour
    {
        [SerializeField] DownedConfig config;
        [SerializeField] InputActionReference interact;

        readonly NetworkVariable<float> progress = new();
        bool holding;   // 서버
        Player me;

        public float Progress01 => progress.Value / config.reviveHoldTime;

        public bool CanRevive(Player partner) =>
            partner != null && partner.Health.IsDead && !me.Health.IsDead &&
            Vector3.Distance(partner.transform.position, transform.position) <= config.reviveRange;

        void Awake() => me = GetComponent<Player>();

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner && NetworkObject.IsPlayerObject && CursorLock.IsLocked)
            {
                if (interact.action.WasPressedThisFrame()) HoldRpc(true);
                if (interact.action.WasReleasedThisFrame()) HoldRpc(false);
            }

            if (!IsServer) return;
            var partner = me.Partner;
            progress.Value = holding && CanRevive(partner) ? progress.Value + Time.deltaTime : 0f;
            if (progress.Value >= config.reviveHoldTime)
            {
                partner.Health.Revive(config.reviveHealthRatio);
                progress.Value = 0f;
            }
        }

        public void SetBotHolding(bool active)
        {
            if (IsServer && me.IsBot) holding = active;
        }

        [Rpc(SendTo.Server)]
        void HoldRpc(bool down) => holding = down;
    }
}
