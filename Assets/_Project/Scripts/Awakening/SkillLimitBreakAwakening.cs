using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 3번(스킬) 파츠 반환 각성 — 독자 스킬 한계 돌파. 스킬 수치를 바꾼다.
    /// P1: 패링 쿨타임 절반 / P2: 무기 전환 선딜레이 0.4초, Q 유도탄 +50%.
    /// (Q 필요 처치 수 15 → 10은 Ultimate가 3번 파츠 상태로 직접 처리한다)
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Awakening/Skill Limit Break")]
    public class SkillLimitBreakAwakening : Awakening
    {
        public float parryCooldownMultiplier = 1f;
        [Tooltip("0 이하면 바꾸지 않는다")]
        public float switchWindup = -1f;
        public float homingRateMultiplier = 1f;

        public override float ParryCooldownMultiplier => parryCooldownMultiplier;
        public override float SwitchWindupOverride => switchWindup;
        public override float HomingRateMultiplier => homingRateMultiplier;
    }
}
