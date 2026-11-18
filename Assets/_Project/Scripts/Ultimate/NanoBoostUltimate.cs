using UnityEngine;

namespace OnYourMark
{
    /// <summary>P1 Q — 나노 강화제: 공격력 · 공격 속도 · 이동 속도 증가, 최대 대쉬 확장 후 전부 충전.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Ultimate/Nano Boost")]
    public class NanoBoostUltimate : UltimateData
    {
        [Range(1, 3)] public float damage = 1.5f;
        [Range(1, 3)] public float attackSpeed = 1.4f;
        [Range(1, 3)] public float moveSpeed = 1.2f;
        [Tooltip("최대 대쉬에 더할 칸 (3 → 5)")]
        public int bonusDashStacks = 2;

        public override float DamageMultiplier => damage;
        public override float AttackSpeedMultiplier => attackSpeed;
        public override float MoveSpeedMultiplier => moveSpeed;

        public override void Begin(Ultimate u) => u.SetDashBonusRpc(bonusDashStacks, true);
        public override void End(Ultimate u) => u.SetDashBonusRpc(0, false);
    }
}
