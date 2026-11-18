using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// Q 궁극기 하나 (전략 패턴). 본인이 직접 처치한 적 수로 충전되고, 발동하면 duration 동안 효과가 켜진다.
    /// 훅은 모두 서버에서 호출된다. 능력치 배율은 무기 · 이동이 읽는다.
    /// </summary>
    public abstract class UltimateData : ScriptableObject
    {
        public string displayName = "ULTIMATE";
        public float duration = 8f;
        [Tooltip("충전에 필요한 본인 처치 수")]
        public int killsRequired = 15;
        [Tooltip("3번(스킬) 파츠를 돌려받은 뒤 필요한 처치 수")]
        public int killsRequiredAwakened = 10;

        [Header("3번(스킬) 파츠를 파트너가 들고 있을 때 (반환 전 시너지)")]
        [Tooltip("발동 중 파트너가 적을 처치할 때마다 늘어나는 지속 시간 (0이면 없음)")]
        public float extendPerPartnerKill;
        [Tooltip("한 번 발동에서 늘어날 수 있는 최대 시간")]
        public float extendMax = 4f;

        public virtual float DamageMultiplier => 1f;
        public virtual float AttackSpeedMultiplier => 1f;
        public virtual float MoveSpeedMultiplier => 1f;

        public virtual void Begin(Ultimate u) { }
        public virtual void Tick(Ultimate u, float dt) { }
        public virtual void End(Ultimate u) { }
    }
}
