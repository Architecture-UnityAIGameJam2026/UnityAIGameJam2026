using UnityEngine;

namespace OnYourMark
{
    /// <summary>R 재장전과 2번 파츠 반환 후 자동 재생 수치.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Support/Reload Config")]
    public class ReloadConfig : ScriptableObject
    {
        [Tooltip("R 재장전 쿨타임 (원격 · 자가 공통)")]
        public float cooldown = 5f;

        [Header("2번 파츠 반환 후 탄창 자동 재생 (P2)")]
        [Tooltip("마지막 사격 후 이 시간이 지나야 재생을 시작한다")]
        public float ammoRegenDelay = 1.5f;
        [Tooltip("탄 1발이 차는 간격")]
        public float ammoRegenInterval = 0.4f;
    }
}
