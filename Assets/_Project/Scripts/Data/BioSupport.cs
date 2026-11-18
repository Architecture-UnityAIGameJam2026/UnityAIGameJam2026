using UnityEngine;

namespace OnYourMark
{
    /// <summary>1 생체 지원: 최대 HP의 일정 비율 즉시 회복 + 잠깐 실드.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Support/Bio")]
    public class BioSupport : SupportEffect
    {
        [Range(0, 1)] public float healRatio = 0.4f;
        public float shield = 30f;
        public float shieldDuration = 2f;

        public override void Apply(Player supporter, Player target)
        {
            target.Health.Heal(target.Health.Max * healRatio);
            target.Health.AddShield(shield, shieldDuration);
        }
    }
}
