using UnityEngine;

namespace OnYourMark
{
    /// <summary>1번 파츠 반환 각성: 적을 처치하면 최대 HP의 일정 비율을 회복한다.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Lifesteal")]
    public class LifestealAwakening : Awakening
    {
        [Range(0, 1)] public float healRatio = 0.05f;

        public override void OnKill(AwakeningSet owner)
        {
            SoundRelay.All(SoundId.HealthAbsorb, owner.Me.transform.position + Vector3.up);
            var health = owner.Me.Health;
            health.Heal(health.Max * healRatio);
        }
    }
}
