using System;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>플레이어를 실어 보내는 이벤트 채널. 예: 적 처치 -> 처치한 플레이어.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Player Event")]
    public class PlayerEvent : ScriptableObject
    {
        public event Action<Player> Raised;
        public void Raise(Player player) => Raised?.Invoke(player);
    }
}
