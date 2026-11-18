using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>교차 HUD 파츠 (새 기획서): 1 체력바+미니맵 / 2 탄창 · 대쉬 수 / 3 스킬(Q · E)</summary>
    public enum HudPart { Vitals, Resource, Skill }

    /// <summary>
    /// 교차 HUD 파츠의 반환 상태 (팀 공용, 서버가 관리).
    /// 반환 전: 각자 상대의 파츠를 들고 있어 상대 정보를 본다. 반환 후: 자기 정보를 본다.
    /// 교환(1/2/3)과 단말기 해금은 언제나 양쪽 동시에 일어나므로 파츠마다 비트 하나면 충분하다.
    /// </summary>
    public class CrossHudState : NetworkBehaviour
    {
        [Tooltip("재시작 시 Raise: 모든 파츠를 다시 상대가 들고 시작")]
        [SerializeField] GameEvent roundReset;

        readonly NetworkVariable<byte> returnedParts = new();

        public override void OnNetworkSpawn()
        {
            if (IsServer) roundReset.Raised += ResetParts;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) roundReset.Raised -= ResetParts;
        }

        void ResetParts() => returnedParts.Value = 0;

        public bool IsReturned(HudPart part) => (returnedParts.Value & Bit(part)) != 0;
        public bool AllReturned => returnedParts.Value == 0b111;

        /// <summary>서버 전용. P9(교환 / 단말기)에서 호출한다.</summary>
        public void Return(HudPart part)
        {
            if (IsServer) returnedParts.Value |= Bit(part);
        }

        static byte Bit(HudPart part) => (byte)(1 << (int)part);
    }
}
