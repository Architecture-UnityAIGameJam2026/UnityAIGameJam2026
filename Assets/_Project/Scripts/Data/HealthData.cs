using UnityEngine;

namespace OnYourMark
{
    /// <summary>체력 수치. 플레이어, 테스트 더미, 적(P6)이 각자 에셋 하나를 참조한다.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Health")]
    public class HealthData : ScriptableObject
    {
        public float maxHealth = 100f;
    }
}
