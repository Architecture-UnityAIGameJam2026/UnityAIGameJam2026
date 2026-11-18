using System;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// ScriptableObject 이벤트 채널. 보내는 쪽과 받는 쪽이 서로를 몰라도 같은 에셋만 참조하면 된다.
    /// (예: RoleSelection이 PlayersSpawned를 Raise -> WaveManager가 웨이브 시작)
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Game Event")]
    public class GameEvent : ScriptableObject
    {
        public event Action Raised;
        public void Raise() => Raised?.Invoke();

        // 도메인 리로드를 끈 플레이 모드: 에셋은 메모리에 남으므로 지난 플레이의 구독자를 비운다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSubscribers()
        {
            foreach (var e in Resources.FindObjectsOfTypeAll<GameEvent>()) e.Raised = null;
        }
    }
}
