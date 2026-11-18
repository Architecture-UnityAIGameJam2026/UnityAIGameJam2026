using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>포지션 하나(P1 Sword / P2 Railgun). 선택 팝업, 스폰 포인트, 스폰할 프리팹이 모두 이 에셋을 기준으로 짝지어진다.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Role")]
    public class RoleDefinition : ScriptableObject
    {
        public string displayName = "P1 · SWORD";
        public string description = "Melee";
        public NetworkObject playerPrefab;
    }
}
