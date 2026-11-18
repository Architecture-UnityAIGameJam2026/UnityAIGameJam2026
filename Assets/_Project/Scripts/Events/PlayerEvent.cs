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

        // 도메인 리로드를 끈 플레이 모드: 에셋은 메모리에 남으므로 지난 플레이의 구독자를 비운다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSubscribers()
        {
            foreach (var e in Resources.FindObjectsOfTypeAll<PlayerEvent>()) e.Raised = null;
        }
    }
}
