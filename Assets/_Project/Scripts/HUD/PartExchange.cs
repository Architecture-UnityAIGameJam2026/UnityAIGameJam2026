using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnYourMark
{
    /// <summary>
    /// 5초 부분 합의 교환. 한 명이 1/2/3 중 하나를 누르면 상대 화면에 제안이 뜨고,
    /// 제한 시간 안에 상대도 같은 키를 누르면 그 파츠만 서로 돌려받는다.
    /// 다른 키를 누르면 새 제안으로 바뀌고, 시간이 지나면 취소된다.
    /// </summary>
    public class PartExchange : NetworkBehaviour
    {
        [SerializeField] UnlockConfig config;
        [SerializeField] CrossHudState state;
        [Tooltip("HudPart 순서 (1 생체, 2 지도, 3 소모품)")]
        [SerializeField] InputActionReference[] swapKeys;

        readonly NetworkVariable<int> offerPart = new(-1);
        readonly NetworkVariable<ulong> offerFrom = new();
        readonly NetworkVariable<double> offerUntil = new();

        public bool HasOffer => offerPart.Value >= 0 && TimeLeft > 0f;
        public HudPart OfferPart => (HudPart)offerPart.Value;
        public ulong OfferFrom => offerFrom.Value;
        public float TimeLeft => IsSpawned ? Mathf.Max(0f, (float)(offerUntil.Value - NetworkManager.ServerTime.Time)) : 0f;
        public float TimeLeft01 => TimeLeft / config.offerDuration;

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer && offerPart.Value >= 0 && TimeLeft <= 0f) offerPart.Value = -1;   // 시간 초과: 취소

            if (Player.Local == null || !CursorLock.IsLocked) return;
            for (int i = 0; i < swapKeys.Length; i++)
                if (swapKeys[i].action.WasPressedThisFrame()) Offer((HudPart)i);
        }

        public void Offer(HudPart part)
        {
            if (IsSpawned && Player.Local != null && (int)part >= 0 && (int)part < 3)
                OfferRpc((int)part);
        }

        public void CancelOffer()
        {
            if (IsSpawned && Player.Local != null) CancelOfferRpc();
        }

        [Rpc(SendTo.Server)]
        void CancelOfferRpc(RpcParams rpc = default)
        {
            if (HasOffer && offerFrom.Value == rpc.Receive.SenderClientId) Cancel(rpc.Receive.SenderClientId);
        }

        [Rpc(SendTo.Server)]
        void OfferRpc(int part, RpcParams rpc = default)
        {
            ApplyOffer(part, rpc.Receive.SenderClientId);
        }

        public bool BotOffer(HudPart part, bool confirm)
        {
            if (!IsServer || state.IsReturned(part)) return false;
            bool match = HasOffer && OfferPart == part && OfferFrom != RoleSelection.BotSlot;
            if (confirm != match || (!confirm && HasOffer)) return false;
            ApplyOffer((int)part, RoleSelection.BotSlot);
            return true;
        }

        public bool BotCancel()
        {
            if (!IsServer || !HasOffer || OfferFrom != RoleSelection.BotSlot) return false;
            Cancel(RoleSelection.BotSlot);
            return true;
        }

        void Report(ulong sender, string action, HudPart part)
        {
            foreach (var player in Player.All)
                if ((player.IsBot ? RoleSelection.BotSlot : player.OwnerClientId) == sender)
                    player.ReportAction(action, part.ToString());
        }

        void Cancel(ulong sender)
        {
            Report(sender, "cancel_exchange", OfferPart);
            offerPart.Value = -1;
        }

        void ApplyOffer(int part, ulong sender)
        {
            if (part < 0 || part >= 3) return;
            if (state.IsReturned((HudPart)part)) return;

            bool partnerOfferedSame = HasOffer && offerPart.Value == part && offerFrom.Value != sender;
            if (partnerOfferedSame)
            {
                state.Return((HudPart)part);
                Report(sender, "confirm_exchange", (HudPart)part);
                offerPart.Value = -1;
                return;
            }

            offerPart.Value = part;
            offerFrom.Value = sender;
            offerUntil.Value = NetworkManager.ServerTime.Time + config.offerDuration;
        }
    }
}
