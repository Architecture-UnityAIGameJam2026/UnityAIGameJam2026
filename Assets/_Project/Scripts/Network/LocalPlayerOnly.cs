using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 내 캐릭터(IsOwner)에서만 켜야 하는 것들(입력, 카메라)을 스폰 시점에 켠다.
    /// 프리팹에서는 꺼 둔 상태로 두어, 상대 캐릭터가 생길 때 내 입력·커서를 건드리지 않게 한다.
    /// 1인칭 시야를 막는 내 몸 렌더러는 소유자 클라이언트에서만 숨긴다.
    /// </summary>
    public class LocalPlayerOnly : NetworkBehaviour
    {
        [SerializeField] Behaviour[] components;
        [SerializeField] GameObject[] objects;
        [SerializeField] Renderer[] ownerHiddenRenderers;

        public override void OnNetworkSpawn()
        {
            bool local = IsOwner && NetworkObject.IsPlayerObject;
            if (components != null)
                foreach (var c in components)
                    if (c != null) c.enabled = local;

            if (objects != null)
                foreach (var o in objects)
                    if (o != null) o.SetActive(local);

            if (ownerHiddenRenderers != null)
                foreach (var r in ownerHiddenRenderers)
                    if (r != null) r.enabled = !local;
        }
    }
}
