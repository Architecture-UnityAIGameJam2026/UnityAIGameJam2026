using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 내 캐릭터(IsOwner)에서만 켜야 하는 것들(입력, 카메라)을 스폰 시점에 켠다.
    /// 프리팹에서는 꺼 둔 상태로 두어, 상대 캐릭터가 생길 때 내 입력·커서를 건드리지 않게 한다.
    /// </summary>
    public class LocalPlayerOnly : NetworkBehaviour
    {
        [SerializeField] Behaviour[] components;
        [SerializeField] GameObject[] objects;

        public override void OnNetworkSpawn()
        {
            foreach (var c in components) c.enabled = IsOwner;
            foreach (var o in objects) o.SetActive(IsOwner);
        }
    }
}
