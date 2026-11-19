using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 서버에서만 알 수 있는 순간(패링 성공, 원격 지원, 단말기 해금 등)의 효과음을 모든 화면에 보낸다.
    /// 씬에 하나 (CrossHud 프리팹). 서버 코드에서 SoundRelay.All(id, 위치)로 부른다.
    /// </summary>
    public class SoundRelay : NetworkBehaviour
    {
        static SoundRelay instance;

        public override void OnNetworkSpawn() => instance = this;

        public override void OnNetworkDespawn()
        {
            if (instance == this) instance = null;
        }

        /// <summary>서버 전용. 모든 클라이언트에서 position에 소리를 낸다.</summary>
        public static void All(SoundId id, Vector3 position)
        {
            if (instance && instance.IsServer) instance.PlayRpc(id, position);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void PlayRpc(SoundId id, Vector3 position) => Sfx.Play(id, position);
    }
}
