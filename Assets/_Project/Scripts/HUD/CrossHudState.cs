using Unity.Netcode;

namespace OnYourMark
{
    public enum HudPart { Bio, Map, Consumable }

    /// <summary>
    /// 교차 HUD 파츠의 반환 상태 (팀 공용, 서버가 관리).
    /// 반환 전: 각자 상대의 파츠를 들고 있어 상대 정보를 본다. 반환 후: 자기 정보를 본다.
    /// 교환(1/2/3)과 단말기 해금은 언제나 양쪽 동시에 일어나므로 파츠마다 비트 하나면 충분하다.
    /// </summary>
    public class CrossHudState : NetworkBehaviour
    {
        readonly NetworkVariable<byte> returnedParts = new();

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
