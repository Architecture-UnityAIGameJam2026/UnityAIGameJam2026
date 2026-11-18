using UnityEngine;

namespace OnYourMark
{
    /// <summary>파츠 반환(교환 / 단말기) 수치.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Unlock Config")]
    public class UnlockConfig : ScriptableObject
    {
        [Tooltip("한 명이 1/2/3을 누른 뒤 상대가 같은 키를 눌러야 하는 제한 시간")]
        public float offerDuration = 5f;
        [Tooltip("단말기에서 두 명이 동시에 E를 누르고 있어야 하는 시간")]
        public float terminalHoldTime = 1.5f;
    }
}
