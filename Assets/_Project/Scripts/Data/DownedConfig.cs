using UnityEngine;

namespace OnYourMark
{
    /// <summary>빈사 · 소생 수치.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Downed Config")]
    public class DownedConfig : ScriptableObject
    {
        public float reviveRange = 2f;
        public float reviveHoldTime = 2.5f;
        [Range(0, 1)] public float reviveHealthRatio = 0.5f;
    }
}
